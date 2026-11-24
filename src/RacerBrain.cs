using System;
using System.Collections.Generic;
using UnityEngine;

namespace WestlandRacerMod
{
    /// <summary>
    /// The auto-player. One finite state machine derived from the server-synced
    /// cache, at most one server action per poll (with a global action
    /// cooldown), everything defensive.
    ///
    ///   Idle  --AutoMatch-->  Matching  --new match data-->  Select (per round)
    ///   Select --request sent & confirmed--> Waiting --> Racing --> Settle --> End --> (Idle, rematch)
    /// </summary>
    internal sealed class RacerBrain
    {
        public enum Phase
        {
            Unknown = 0,
            Idle,          // no live room data -> between matches / on the event panel
            Matching,      // MatchRequest outstanding
            Select,        // Vehicle Selection Phase (Stage 0)
            Waiting,       // Enter/Ready (Stage 1/2) - countdowns, nothing to send yet
            Racing,        // Stage 3 - playing cards and props
            Settle,        // Stage 4 - round results
            End            // Stage 5 - match finished
        }

        private readonly RacerSettings _settings;
        private readonly RacerStats _stats = new RacerStats();
        private readonly object _stateLock = new object();

        private float _pollTimer;
        private Phase _phase = Phase.Unknown;
        private RaceSnapshot _lastSnap;
        private string _lastStatus = "starting";

        // Match tracking (match key = GameBaseData.BeginTime)
        private long _matchKey;
        private bool _matchKeyKnown;
        private readonly HashSet<uint> _usedCarIds = new HashSet<uint>();
        private readonly HashSet<uint> _failedCarIds = new HashSet<uint>();

        // Matching flow
        private bool _matchRequested;
        private float _matchRequestedAt;
        private float _matchBackoffUntil;
        private float _raceFinishedAt = float.MinValue;

        // Select phase
        private uint _selectSentRound;
        private float _selectSentAt;
        private uint _tentativeCar;
        private bool _selectConfirmedLogged;

        // Racing
        private float _cardCdUntil;
        private float _cardPlayBackoffUntil;
        private string _pendingHandSignature;   // expected hand after our last play (sorted, joined)
        private float _pendingHandAt;
        private int _cardRejectsInRound;        // consecutive rejects -> latch off for the round
        private bool _cardPlayLatchOpen = true;
        private readonly Dictionary<uint, float> _propFailUntil = new Dictionary<uint, float>();
        private readonly Dictionary<uint, float> _propNextUse = new Dictionary<uint, float>();
        private string _raceStartRoundKey;
        private readonly HashSet<uint> _raceStartAttempted = new HashSet<uint>();
        private float _enteredRaceAt;
        private int _prevStage = -1;

        // Global action pacing
        private float _lastActionAt = float.MinValue;

        // SmartPlay v2 state
        private string _lastModeText = "starting";
        private int _heldSinglesCount;
        private string _lastHoldReason = "";
        private float _lastHoldLogAt;

        // Logging
        private string _lastComboText = "";
        private string _lastActionText = "";

        public RacerBrain(RacerSettings settings) { _settings = settings; }

        public RacerStats Stats { get { return _stats; } }
        public Phase CurrentPhase { get { lock (_stateLock) { return _phase; } } }
        public RaceSnapshot LastSnapshot { get { lock (_stateLock) { return _lastSnap; } } }
        public string LastStatus { get { lock (_stateLock) { return _lastStatus; } } }
        public string LastComboText { get { lock (_stateLock) { return _lastComboText; } } }
        public string LastActionText { get { lock (_stateLock) { return _lastActionText; } } }
        public string LastModeText { get { lock (_stateLock) { return _lastModeText; } } }
        public int HeldSinglesCount { get { lock (_stateLock) { return _heldSinglesCount; } } }
        public string LastHoldReason { get { lock (_stateLock) { return _lastHoldReason; } } }
        public bool MatchRequested { get { lock (_stateLock) { return _matchRequested; } } }

        // ------------------------------------------------------------------
        // Entry point
        // ------------------------------------------------------------------

        public void Tick(float deltaTime)
        {
            if (!_settings.Enabled)
            {
                lock (_stateLock) { _phase = Phase.Unknown; _lastStatus = "disabled"; }
                return;
            }

            _pollTimer -= deltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = _settings.DecisionInterval;

            try { Step(); }
            catch (Exception ex) { Log.WarnOnce("Step: " + ex.Message); }
        }

        private void Step()
        {
            float now = Time.time;
            var snap = RaceApi.Capture();
            lock (_stateLock) { _lastSnap = snap; }

            // ---- Match key handling ---------------------------------------
            if (snap.GameBeginTimeKnown && snap.GameBeginTime != 0)
            {
                if (!_matchKeyKnown || snap.GameBeginTime != _matchKey)
                {
                    _matchKey = snap.GameBeginTime;
                    _matchKeyKnown = true;
                    _usedCarIds.Clear();
                    _failedCarIds.Clear();
                    _selectSentRound = 0;
                    _tentativeCar = 0;
                    // Note: _matchRequested is cleared by the phase derivation once a live stage is seen,
                    // NOT here - clearing it here makes the mod queue a duplicate match request.
                    _raceStartRoundKey = null;
                    _raceStartAttempted.Clear();
                    _propNextUse.Clear();
                    _propFailUntil.Clear();
                    Log.Info("New match detected (key=" + _matchKey + "); per-match state reset.");
                }
            }

            // ---- Phase derivation ------------------------------------------
            Phase phase;
            if (!snap.RoomFound || snap.Stage < 0)
            {
                phase = _matchRequested ? Phase.Matching : Phase.Idle;
                // Match data arrived -> the room will show up; nothing else to do.
            }
            else
            {
                // Only clear "match requested" when we observe a live race forming
                // (entering/selecting/racing/settling). A stale finished-room keeps
                // the flag so we never double-queue (each request can cost a pass).
                if (_matchRequested && snap.Stage >= 0 && snap.Stage <= 4)
                    _matchRequested = false;
                switch (snap.Stage)
                {
                    case 0: phase = Phase.Select; break;
                    case 1:
                    case 2: phase = Phase.Waiting; break;
                    case 3: phase = Phase.Racing; break;
                    case 4: phase = Phase.Settle; break;
                    case 5: phase = Phase.End; break;
                    default: phase = Phase.Unknown; break;
                }
            }

            // Stage transitions
            int stage = snap.Stage;
            if (stage != _prevStage)
            {
                if (stage == 3)
                {
                    _enteredRaceAt = now;
                    _raceStartRoundKey = MatchRoundKey(snap);
                    _raceStartAttempted.Clear();
                    _cardCdUntil = 0f;
                    _cardRejectsInRound = 0;
                    _cardPlayLatchOpen = true;
                    Log.Info("Round " + snap.RoundId + " racing phase started (position " + snap.SelfPosition + "/road, hand=" + snap.Hand.Count
                        + "). RoundBaseData: Select=" + snap.RoundSelectTime + " Enter=" + snap.RoundEnterTime + " Ready=" + snap.RoundReadyTime
                        + " Game=" + snap.RoundGameTime + " Settle=" + snap.RoundSettleTime + " End=" + snap.RoundEndTime
                        + " | GameSwitchTime=" + snap.GameSwitchTime + " GameBeginTime=" + snap.GameBeginTime + " CardCd=" + snap.SelfCardCd);
                }
                else if (stage == 5 || stage == 4)
                {
                    _raceFinishedAt = now;
                }
                else if (stage == 0 && (_prevStage == 4 || _prevStage == 5))
                {
                    Log.Info("Next round selection phase (round " + snap.RoundId + ").");
                }
                _prevStage = stage;
            }

            lock (_stateLock) { _phase = phase; }
            if (phase != Phase.Racing)
            {
                lock (_stateLock)
                {
                    _lastModeText = phase == Phase.Idle ? "idle"
                        : phase == Phase.Matching ? "matching"
                        : phase == Phase.Select ? "selecting"
                        : phase == Phase.Waiting ? "waiting"
                        : phase == Phase.Settle ? "settled"
                        : phase == Phase.End ? "end"
                        : "-";
                }
            }

            // ---- Actions (at most one per poll) -----------------------------
            switch (phase)
            {
                case Phase.Idle:
                    MaybeAutoMatch(now, "idle");
                    break;
                case Phase.Matching:
                    if (now - _matchRequestedAt > 120f)
                    {
                        _matchRequested = false;
                        Log.Info("Match request went unanswered for 120s; allowing retry.");
                    }
                    break;
                case Phase.Select:
                    MaybeSelectCar(now, snap);
                    break;
                case Phase.Racing:
                    var urg = ComputeUrgency(snap, _settings);
                    lock (_stateLock) { _lastModeText = ModeText(urg, snap); }
                    // Once we have scored this round, cards are dead weight - hold them for the next round.
                    if (snap.SettleKnown)
                    {
                        lock (_stateLock)
                        {
                            _lastStatus = "round settled; saving cards and props for the next round";
                        }
                        break;
                    }
                    var bestPlay = (_settings.SmartPlay || _settings.AutoUseProps) && snap.Hand.Count > 0
                        ? ComboEvaluator.Decide(snap.Hand, Math.Max(1, _settings.MaxCardsPerPlay))
                        : default(ComboEvaluator.PlayDecision);
                    if (HandleRaceStart(now, snap)) break;
                    if (_settings.AutoUseProps && TryUseBestProp(now, snap, bestPlay)) break;
                    if (_settings.AutoPlayCards) TryPlayCards(now, snap, urg, bestPlay);
                    break;
                case Phase.End:
                    // The result panel usually closes back to the event page on its
                    // own. Send at most ONE automatic MatchRequest per finished race
                    // (never a stream of them - each accepted request can cost a pass).
                    if (_settings.AutoMatch && now - _raceFinishedAt >= _settings.RematchDelay)
                        MaybeAutoMatch(now, "after race");
                    break;
                default:
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Auto match
        // ------------------------------------------------------------------

        private void MaybeAutoMatch(float now, string reason)
        {
            if (!_settings.AutoMatch)
            {
                lock (_stateLock) { _lastStatus = "idle (auto-match off)"; }
                return;
            }
            if (_matchRequested) return;
            if (now < _matchBackoffUntil)
            {
                lock (_stateLock) { _lastStatus = "idle (match refused by server; backing off " + (int)(_matchBackoffUntil - now) + "s)"; }
                return;
            }
            if (now - _lastActionAt < _settings.ActionCooldown) return;

            if (RaceApi.SendMatchRequest())
            {
                _matchRequested = true;
                _matchRequestedAt = now;
                _lastActionAt = now;
                _stats.MatchesStarted++;
                lock (_stateLock) { _lastStatus = "match requested (" + reason + ")"; }
                Log.Info("MatchRequest sent (" + reason + "). Waiting for MatchReply...");
            }
        }

        // ------------------------------------------------------------------
        // Vehicle Selection Phase
        // ------------------------------------------------------------------

        private void MaybeSelectCar(float now, RaceSnapshot snap)
        {
            lock (_stateLock) { _lastStatus = "selecting vehicle"; }

            if (!_settings.AutoSelectCar) return;
            if (!snap.SelfFound)
            {
                // Round player vo not ready yet; wait a beat.
                return;
            }

            uint roundId = snap.RoundId;
            if (snap.SelectCarId != 0)
            {
                // We already have a car this round (possibly picked by the UI before us).
                if (!_selectConfirmedLogged)
                {
                    _selectConfirmedLogged = true;
                    _tentativeCar = snap.SelectCarId;
                    _usedCarIds.Add(snap.SelectCarId);
                    Log.Info("Round " + roundId + ": car already confirmed (id=" + snap.SelectCarId + ").");
                }
                _selectSentRound = roundId;
                return;
            }
            _selectConfirmedLogged = false;

            if (_selectSentRound == roundId && now - _selectSentAt < 4f)
                return; // just asked; give the server a moment

            uint pick = ChooseCar();
            if (pick == 0)
            {
                lock (_stateLock) { _lastStatus = "no selectable car left (all rejected/used)"; }
                return;
            }
            if (now - _lastActionAt < _settings.ActionCooldown) return;

            if (RaceApi.SendSelectCarRequest(pick))
            {
                _selectSentRound = roundId;
                _selectSentAt = now;
                _tentativeCar = pick;
                _lastActionAt = now;
                _usedCarIds.Add(pick);
                lock (_stateLock) { _lastStatus = "asked for car " + CarName(pick); }
                Log.Info("SelectCarRequest sent for " + CarName(pick) + " (round " + roundId + ").");
            }
        }

        private uint ChooseCar()
        {
            // Best first: Dickey Z4 (T) > Bourbon 1500 (S) > Beetle (A).
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < GameConfig.Cars.Length; i++)
                {
                    uint id = GameConfig.Cars[i].Id;
                    if (_failedCarIds.Contains(id)) continue;
                    if (pass == 0 && _usedCarIds.Contains(id)) continue; // unused first
                    return id;
                }
            }
            return 0;
        }

        private static string CarName(uint id)
        {
            for (int i = 0; i < GameConfig.Cars.Length; i++)
                if (GameConfig.Cars[i].Id == id) return GameConfig.Cars[i].Name;
            return "car #" + id;
        }

        public static string CarGrade(uint id)
        {
            for (int i = 0; i < GameConfig.Cars.Length; i++)
                if (GameConfig.Cars[i].Id == id)
                    return id == GameConfig.CarDickeyZ4 ? "T" : (id == GameConfig.CarBourbon1500 ? "S" : "A");
            return "?";
        }

        // ------------------------------------------------------------------
        // Urgency model (SmartPlay v2)
        // ------------------------------------------------------------------

        private readonly struct Urgency
        {
            public readonly int MyRank;
            public readonly long Remaining;
            public readonly long NextStation;   // distance to next supply station (long.MaxValue when none)
            public readonly long LeaderGap;     // distance to the leader; 0 when we are the leader
            public readonly long SecondGap;     // our lead over second place; 0 when we trail
            public readonly bool Behind;
            public readonly bool LeadingBig;
            public readonly bool Endgame;

            public Urgency(int myRank, long remaining, long nextStation, long leaderGap, long secondGap,
                bool behind, bool leadingBig, bool endgame)
            {
                MyRank = myRank; Remaining = remaining; NextStation = nextStation;
                LeaderGap = leaderGap; SecondGap = secondGap;
                Behind = behind; LeadingBig = leadingBig; Endgame = endgame;
            }
        }

        private static Urgency ComputeUrgency(RaceSnapshot snap, RacerSettings s)
        {
            const long RoadLong = 2000;
            long self = snap.SelfPosition;
            long leaderGap = 0, secondGap = 0;
            long bestAhead = 0, bestBehind = long.MinValue;
            int rank = 1;
            for (int i = 0; i < snap.Players.Count; i++)
            {
                var p = snap.Players[i];
                if (p.IsSelf) continue;
                if (p.Position > self) { rank++; if (p.Position > bestAhead) bestAhead = p.Position; }
                else if (p.Position > bestBehind) bestBehind = p.Position;
            }
            if (rank > 1) leaderGap = bestAhead - self;
            else secondGap = bestBehind == long.MinValue ? RoadLong : self - bestBehind;

            long remaining = RoadLong - self;
            long nextStation = long.MaxValue;
            foreach (long st in StationPositions)
                if (st > self) { nextStation = st - self; break; }

            bool behind = rank > 1;
            bool leadingBig = rank == 1 && secondGap >= s.CruiseLeadThreshold;
            bool endgame = remaining <= s.EndgameDistance;
            return new Urgency(rank, remaining, nextStation, leaderGap, secondGap, behind, leadingBig, endgame);
        }

        private static readonly long[] StationPositions = { 250L, 500L, 750L };

        private string ModeText(Urgency urg, RaceSnapshot snap)
        {
            if (snap.SettleKnown) return "locked";
            if (urg.Endgame) return "endgame";
            if (urg.Behind) return "behind";
            if (urg.LeadingBig) return "cruise";
            return "neutral";
        }

        // ------------------------------------------------------------------
        // Racing phase
        // ------------------------------------------------------------------

        private string MatchRoundKey(RaceSnapshot snap)
        {
            return (_matchKeyKnown ? _matchKey.ToString() : "m") + ":r" + snap.RoundId;
        }

        /// <summary>Race-start package, fired once per racing stage: Canister -> Coolant Leak -> Ion Shield -> Fuel.</summary>
        private bool HandleRaceStart(float now, RaceSnapshot snap)
        {
            string key = MatchRoundKey(snap);
            if (_raceStartRoundKey != key)
            {
                _raceStartRoundKey = key;
                _raceStartAttempted.Clear();
            }

            // Race-start priorities: block slows, raw speed. The Power Canister is
            // saved for the first big combo instead (SmartPlay), Coolant Leak only
            // on request.
            var package = new List<uint>
            {
                GameConfig.PropIonShield,       // block one slow
                GameConfig.PropHighOctaneFuel,  // pure speed
            };
            if (_settings.UseCoolantLeak)
                package.Insert(0, GameConfig.PropCoolantLeak);   // weaken everyone's cards
            if (!_settings.SmartPlay)
                package.Insert(0, GameConfig.PropPowerCanister); // legacy: buff cards early

            foreach (uint propId in package)
            {
                if (_raceStartAttempted.Contains(propId)) continue;
                if (!HasFreeProp(snap, propId, now))
                {
                    _raceStartAttempted.Add(propId); // can't use it this round; don't keep checking
                    continue;
                }
                if (now - _lastActionAt < _settings.ActionCooldown)
                    return false; // wait for the action cooldown instead of dropping the prop
                if (TryUseProp(now, propId, snap))
                {
                    _raceStartAttempted.Add(propId);
                    return true;
                }
                return false;
            }
            // Package complete for this round.
            return false;
        }

        private bool TryUseBestProp(float now, RaceSnapshot snap, ComboEvaluator.PlayDecision bestPlay)
        {
            if (now - _lastActionAt < _settings.ActionCooldown) return false;

            // 1) Cleanse when we are actually debuffed.
            bool debuffed = false;
            if (snap.BuffSkillTypes != null)
            {
                for (int i = 0; i < snap.BuffSkillTypes.Count; i++)
                {
                    int t = snap.BuffSkillTypes[i];
                    if (t == GameConfig.SkillTypeSpeedDebuff || t == GameConfig.SkillTypeCardDebuff) { debuffed = true; break; }
                }
            }
            if (!_settings.RequireDebuffForRoadClearer || debuffed)
            {
                if (TryUseProp(now, GameConfig.PropRoadClearer, snap)) return true;
            }

            // 2) Homing Missile only when the leader is someone else and ahead of us.
            var leader = snap.Leader;
            if (leader != null && !leader.IsSelf)
            {
                long gap = leader.Position - snap.SelfPosition;
                if (gap >= _settings.MissileLeadThreshold)
                {
                    if (TryUseProp(now, GameConfig.PropHomingMissile, snap)) return true;
                }
            }

            // 3) Card Ace when the hand is not full.
            if ((long)snap.Hand.Count < (long)snap.HandCardsLimit)
            {
                if (TryUseProp(now, GameConfig.PropCardAce, snap)) return true;
            }

            // 4) Sludge Oil when someone ahead of us (even if we lead others).
            bool someoneAhead = false;
            for (int i = 0; i < snap.Players.Count; i++)
            {
                if (!snap.Players[i].IsSelf && snap.Players[i].Position > snap.SelfPosition) { someoneAhead = true; break; }
            }
            if (someoneAhead)
            {
                if (TryUseProp(now, GameConfig.PropSludgeOil, snap)) return true;
            }

            // 5) Power Canister reserved for a real combo on board (never a bare single).
            if (bestPlay.CardIds != null && bestPlay.BestCombo.Id != GameConfig.ComboSingle)
            {
                if (TryUseProp(now, GameConfig.PropPowerCanister, snap)) return true;
            }

            // 6) If nobody is ahead of us (we lead), still worth slowing everyone.
            if (TryUseProp(now, GameConfig.PropSludgeOil, snap)) return true;
            if (TryUseProp(now, GameConfig.PropHighOctaneFuel, snap)) return true;
            if (_settings.UseCoolantLeak)
            {
                if (TryUseProp(now, GameConfig.PropCoolantLeak, snap)) return true;
            }

            // 7) Paid props - opt-in only.
            if (_settings.UsePaidProps)
            {
                if (TryUseProp(now, GameConfig.PropCardUpgrade, snap)) return true;
                if (TryUseProp(now, GameConfig.PropSyncCore, snap)) return true;
                if (TryUseProp(now, GameConfig.PropCardThief, snap)) return true;
                if (TryUseProp(now, GameConfig.PropBlindBox, snap)) return true;
                if (TryUseProp(now, GameConfig.PropShuffle, snap)) return true;
            }

            return false;
        }

        private bool TryUseProp(float now, uint propId, RaceSnapshot snap)
        {
            if (propId == 0) return false;
            if (!HasFreeProp(snap, propId, now)) return false;
            if (now - _lastActionAt < _settings.ActionCooldown) return false;

            if (RaceApi.SendUsePropRequest(propId, 0))
            {
                _lastActionAt = now;
                _propNextUse[propId] = now + 5.2f;
                _stats.PropsUsed++;
                string name = GameConfig.PropName(propId);
                lock (_stateLock) { _lastActionText = "used " + name; }
                Log.Info("UsePropRequest sent: " + name + " (id=" + propId + ").");
                return true;
            }
            return false;
        }

        private bool HasFreeProp(RaceSnapshot snap, uint propId, float now)
        {
            float failUntil;
            if (_propFailUntil.TryGetValue(propId, out failUntil) && now < failUntil) return false;
            float nextUse;
            if (_propNextUse.TryGetValue(propId, out nextUse) && now < nextUse) return false;
            for (int i = 0; i < snap.FreeProps.Count; i++)
                if (snap.FreeProps[i] == propId) return true;
            return false;
        }

        private static string HandSignature(IReadOnlyList<uint> hand)
        {
            var copy = new List<uint>(hand);
            copy.Sort();
            return string.Join(",", copy);
        }

        private bool TryPlayCards(float now, RaceSnapshot snap, Urgency urg, ComboEvaluator.PlayDecision precomputed)
        {
            if (!_cardPlayLatchOpen) return false;
            if (now < _cardCdUntil) return false;
            if (now < _cardPlayBackoffUntil) return false;
            if (now - _lastActionAt < _settings.ActionCooldown) return false;
            if (snap.Hand.Count == 0) return false;

            // The hand snapshot only refreshes when the server pushes a new StatusData.
            // Until then, replaying the exact same cards would just be rejected -
            // wait for the post-play hand (or a 3s timeout) before playing again.
            if (_pendingHandSignature != null)
            {
                if (HandSignature(snap.Hand) == _pendingHandSignature || now - _pendingHandAt > 3f)
                    _pendingHandSignature = null; // server applied the play (or we stopped waiting)
                else
                    return false;
            }

            var decision = precomputed.CardIds != null
                ? precomputed
                : ComboEvaluator.Decide(snap.Hand, Math.Max(1, _settings.MaxCardsPerPlay));
            if (decision.CardIds == null || decision.CardIds.Count == 0) return false;

            // ---- SmartPlay v2 policy ----------------------------------------
            if (_settings.SmartPlay && decision.BestCombo.Id == GameConfig.ComboSingle)
            {
                bool full = snap.Hand.Count >= snap.HandCardsLimit;
                bool hold = false;
                string reason = "";

                if (full)
                {
                    // Hand is full - replenishment is disabled, we must shed.
                }
                else if (urg.Endgame)
                {
                    // Final stretch of the road - dump everything.
                }
                else if (urg.Behind)
                {
                    // Someone is ahead of us (or close ahead) - tempo matters.
                }
                else if (urg.LeadingBig)
                {
                    hold = true; reason = "cruise: leading comfortably; holding weak single";
                }
                else if (urg.NextStation <= _settings.StationHoldDistance)
                {
                    hold = true; reason = "holding for a supply station at +" + urg.NextStation;
                }

                if (hold)
                {
                    lock (_stateLock)
                    {
                        _lastHoldReason = reason;
                        _heldSinglesCount++;
                        _lastStatus = "hold: " + reason;
                    }
                    if (now - _lastHoldLogAt > 10f)
                    {
                        _lastHoldLogAt = now;
                        Log.Info("SmartPlay hold: " + reason + " (single " + decision.SumPoints + ", rank=" + urg.MyRank
                            + ", remaining=" + urg.Remaining + ", nextStation=" + (urg.NextStation == long.MaxValue ? "-" : urg.NextStation.ToString()) + ")");
                    }
                    return false;
                }
            }

            if (!RaceApi.SendUseCardRequest(decision.CardIds)) return false;

            _lastActionAt = now;
            _cardCdUntil = now + _settings.CardCooldown;

            // Remember the hand we expect after the server applies the play,
            // so the next polls don't re-fire the same cards.
            var remaining = new List<uint>(snap.Hand);
            for (int i = 0; i < decision.CardIds.Count; i++)
                remaining.Remove(decision.CardIds[i]);
            _pendingHandSignature = HandSignature(remaining);
            _pendingHandAt = now;
            lock (_stateLock)
            {
                _lastComboText = decision.ToString();
                _lastActionText = "played " + decision.CardIds.Count + " cards (" + decision.BestCombo.Name + ")";
            }
            _stats.RegisterPlay(decision);
            if (_settings.LogPlayDecisions)
                Log.Info("UseCardRequest sent: " + decision);
            return true;
        }

        // ------------------------------------------------------------------
        // Reply observers (called from the Harmony patches, possibly on the
        // game's network thread - keep them tiny)
        // ------------------------------------------------------------------

        internal void OnMatchReply(int errorCode)
        {
            lock (_stateLock)
            {
                if (errorCode == 0)
                {
                    _lastStatus = "match confirmed; waiting for room...";
                    // Keep _matchRequested until the room data shows up (or the 120s stale timer fires).
                    _lastActionAt = Time.time;
                }
                else
                {
                    _matchRequested = false;
                    _matchBackoffUntil = Time.time + 120f;
                    _lastStatus = "match refused by server (code " + errorCode + "); backing off 120s";
                    Log.Warn("MatchReply errorCode=" + errorCode + ". Backing off for 120s.");
                }
            }
        }

        internal void OnSelectCarReply(uint carId, int errorCode)
        {
            lock (_stateLock)
            {
                if (errorCode == 0)
                {
                    Log.Info("SelectCarReply: car " + CarName(carId) + " accepted.");
                }
                else
                {
                    _failedCarIds.Add(carId);
                    _usedCarIds.Remove(carId);
                    _selectSentRound = 0; // allow the picker to try again with another car
                    _lastStatus = "car " + carId + " rejected (code " + errorCode + "); retrying another";
                    Log.Warn("SelectCarReply errorCode=" + errorCode + " for car " + carId + " (" + CarName(carId) + "). Blacklisting it this match.");
                }
            }
        }

        internal void OnUseCardReply(int errorCode, int cardsCount)
        {
            if (errorCode == 0)
            {
                lock (_stateLock)
                {
                    _cardRejectsInRound = 0;
                    _cardPlayLatchOpen = true;
                    _lastStatus = "cards accepted";
                }
                return;
            }
            lock (_stateLock)
            {
                _cardRejectsInRound++;
                _cardPlayBackoffUntil = Time.time + 5f;
                _lastStatus = "card play rejected (code " + errorCode + ")";
                if (_cardRejectsInRound >= 3 && _cardPlayLatchOpen)
                {
                    _cardPlayLatchOpen = false;
                    Log.Warn("3 consecutive card-play rejections (last code " + errorCode + ") - card auto-play OFF for this round. "
                        + "The manual UI path will tell us what the server accepts; check the [api-out] log lines for the exact payload a hand play produces.");
                }
            }
            Log.Warn("UseCardReply errorCode=" + errorCode + " (cards=" + cardsCount + "). Cooling down 5s.");
        }

        internal void OnUsePropReply(uint propId, int errorCode)
        {
            if (errorCode == 0) return;
            lock (_stateLock)
            {
                _propFailUntil[propId] = Time.time + 30f;
            }
            Log.Warn("UsePropReply errorCode=" + errorCode + " for " + GameConfig.PropName(propId) + " (id=" + propId + "). Skipping it for 30s.");
        }

        internal void OnSelfSettled(uint roundId, uint rank, long totalScore, uint totalRank)
        {
            _stats.RegisterSettle(roundId, rank, totalScore, totalRank);
            if (totalRank != 0)
            {
                _raceFinishedAt = Time.time; // match fully settled
            }
            Log.Info("Round " + roundId + " settled: rank=" + rank
                     + (totalRank != 0 ? ", final total rank=" + totalRank : "")
                     + " (totalScore=" + totalScore + ").");
        }
    }
}
