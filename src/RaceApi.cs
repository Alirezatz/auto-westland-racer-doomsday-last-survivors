using System;
using System.Collections.Generic;
using Il2CppIGG.Game.Data.Cache;
using Il2CppIGG.Game.Data.Cache.RaceCar;
using Il2CppIGG.Game.Module.RaceCar;

namespace WestlandRacerMod
{
    /// <summary>One opponent (or self) on the track.</summary>
    internal sealed class PlayerSnapshot
    {
        public ulong PlayerId;
        public long Position;   // server units along the road (road length 2000)
        public long Speed;
        public bool IsSelf;
        public bool IsAi;
    }

    /// <summary>
    /// Flat, managed-only snapshot of everything the brain needs. Values are
    /// copied out of the Il2Cpp objects in one place so the rest of the code
    /// never touches interop wrappers and can never crash mid-frame because a
    /// native object got disposed underneath us.
    /// </summary>
    internal sealed class RaceSnapshot
    {
        public bool CacheReady;
        public uint ActivityId;
        public string Error;

        // Room / round
        public bool RoomFound;
        public int Stage = -1;              // RaceCarRoundStage: 0 Select, 1 Enter, 2 Ready, 3 Game, 4 Settle, 5 End
        public uint RoundId;
        public uint CurRound;               // GameBaseData.CurRound (1..3) when known
        public long GameBeginTime;          // match key: changes when a new match starts
        public bool GameBeginTimeKnown;
        public long GameSwitchTime;
        // Raw round timestamps - logged during races to learn the server's timing.
        public long RoundSelectTime, RoundEnterTime, RoundReadyTime, RoundGameTime, RoundSettleTime, RoundEndTime;
        public long SelfStationTime;

        // Self
        public bool SelfFound;
        public ulong SelfId;
        public long SelfPosition;
        public long SelfSpeed;
        public long SelfCardCd;             // raw value reported by the server (informational)
        public uint StationId;
        public uint SelectCarId;
        public uint HandCardsLimit;
        public List<uint> Hand = new List<uint>();
        public List<uint> FreeProps = new List<uint>();   // props the server considers usable right now
        public List<int> BuffSkillTypes = new List<int>(); // skill types of buffs active on self

        // Self settle
        public bool SettleKnown;
        public uint SettleRank;
        public long SettleScore;
        public long SettleTotalScore;
        public uint SettleTotalRank;
        public bool SettleComplete;

        // Field
        public List<PlayerSnapshot> Players = new List<PlayerSnapshot>();

        public PlayerSnapshot Leader
        {
            get
            {
                PlayerSnapshot best = null;
                for (int i = 0; i < Players.Count; i++)
                {
                    var p = Players[i];
                    if (best == null || p.Position > best.Position) best = p;
                }
                return best;
            }
        }
    }

    /// <summary>
    /// Every read/write against the game happens here. Mirrors the game UI:
    /// it only calls the same public module senders the buttons call
    /// (<c>RaceCarModule.API.SendRaceCar*Request</c>) and reads the same caches the
    /// panels read. All entries swallow exceptions and report failure instead of
    /// letting the mod take the game down.
    /// </summary>
    internal static class RaceApi
    {
        // ------------------------------------------------------------------
        // Context
        // ------------------------------------------------------------------

        /// <summary>The activity id the current mode is running as (191150 for the Wasteland Racer event).</summary>
        public static uint DetectActivityId()
        {
            try
            {
                if (RaceCarModule.API.IsInRaceCarMode(out uint actId, out ulong _))
                {
                    if (actId != 0) return actId;
                }
            }
            catch { /* not in mode / not ready */ }
            return GameConfig.ActivityId;
        }

        public static ulong GetLocalPlayerId()
        {
            try
            {
                var player = AppCache.Player;
                return player != null ? player.PlayerId : 0ul;
            }
            catch { return 0ul; }
        }

        // ------------------------------------------------------------------
        // Snapshot
        // ------------------------------------------------------------------

        // Diagnostic dedup: log hand changes only once (raw ids + server cd).
        private static string _lastLoggedHandSig;
        private static uint _lastLoggedRound;

        private static void LogDebugIfChanged(RaceSnapshot snap)
        {
            try
            {
                if (!snap.SelfFound) return;
                var sorted = new List<uint>(snap.Hand);
                sorted.Sort();
                string sig = string.Join(",", sorted);
                if (sig != _lastLoggedHandSig)
                {
                    _lastLoggedHandSig = sig;
                    Log.Info("Hand raw ids: [" + sig + "] count=" + snap.Hand.Count
                        + " CardCd=" + snap.SelfCardCd + " StationId=" + snap.StationId
                        + " StationTime=" + snap.SelfStationTime);
                }
                if (_lastLoggedRound != snap.RoundId && snap.RoundId != 0)
                {
                    _lastLoggedRound = snap.RoundId;
                    Log.Info("Round " + snap.RoundId + " stage=" + snap.Stage + " times"
                        + " Select=" + snap.RoundSelectTime + " Enter=" + snap.RoundEnterTime
                        + " Ready=" + snap.RoundReadyTime + " Game=" + snap.RoundGameTime
                        + " Settle=" + snap.RoundSettleTime + " End=" + snap.RoundEndTime
                        + " | GameSwitchTime=" + snap.GameSwitchTime + " GameBeginTime=" + snap.GameBeginTime);
                }
            }
            catch { }
        }

        public static RaceSnapshot Capture()
        {
            var snap = new RaceSnapshot();
            try
            {
                snap.ActivityId = DetectActivityId();
                var cache = AppCache.RaceCar;
                if (cache == null)
                {
                    snap.Error = "AppCache.RaceCar is null (not in game yet?)";
                    return snap;
                }
                snap.CacheReady = true;

                RaceCarRoom room = null;
                try { room = cache.GetRoomVo(snap.ActivityId); } catch (Exception ex) { snap.Error = "GetRoomVo: " + ex.Message; }
                if (room == null) return snap;
                snap.RoomFound = true;

                try
                {
                    var gbd = room.RcGameBaseData;
                    if (gbd != null)
                    {
                        snap.CurRound = gbd.CurRound;
                        snap.GameBeginTime = gbd.BeginTime;
                        snap.GameBeginTimeKnown = gbd.BeginTime != 0;
                        snap.GameSwitchTime = gbd.SwitchTime;
                    }
                }
                catch (Exception ex) { Log.WarnOnce("read RcGameBaseData: " + ex.Message); }

                var roundVo = room.GetCurRoundVo();
                if (roundVo == null) return snap;

                var baseData = roundVo.BaseData;
                if (baseData != null)
                {
                    snap.Stage = baseData.Stage;
                    snap.RoundId = baseData.RoundId;
                    snap.RoundSelectTime = baseData.SelectTime;
                    snap.RoundEnterTime = baseData.EnterTime;
                    snap.RoundReadyTime = baseData.ReadyTime;
                    snap.RoundGameTime = baseData.GameTime;
                    snap.RoundSettleTime = baseData.SettleTime;
                    snap.RoundEndTime = baseData.EndTime;
                }

                // All players (positions / AI flags)
                try
                {
                    var all = roundVo.GetAllRoundPlayerData();
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            var vo = all[i];
                            if (vo == null) continue;
                            var p = new PlayerSnapshot { PlayerId = vo.PlayerId };
                            var st = vo.PlayerStatusData;
                            if (st != null) { p.Position = st.Position; p.Speed = st.Speed; }
                            p.IsSelf = false;
                            // AI flag comes from the match/game player list; it may be unavailable, so default false.
                            try
                            {
                                var gp = room.GetGamePlayerDataById(p.PlayerId);
                                if (gp != null) p.IsAi = gp.Ai;
                            }
                            catch { }
                            snap.Players.Add(p);
                        }
                    }
                }
                catch (Exception ex) { Log.WarnOnce("read players: " + ex.Message); }

                var self = roundVo.GetSelfRoundPlayerDataVo();
                if (self != null)
                {
                    snap.SelfFound = true;
                    snap.SelfId = self.PlayerId;
                    // Now that we know who we are, fix up the IsSelf flags.
                    for (int i = 0; i < snap.Players.Count; i++)
                        snap.Players[i].IsSelf = snap.Players[i].PlayerId == snap.SelfId;

                    var st = self.PlayerStatusData;
                    if (st != null)
                    {
                        snap.SelfPosition = st.Position;
                        snap.SelfSpeed = st.Speed;
                        snap.SelfCardCd = st.CardCd;
                        snap.StationId = st.StationId;
                        snap.SelfStationTime = st.StationTime;

                        var cards = st.Cards;
                        if (cards != null)
                        {
                            for (int i = 0; i < cards.Count; i++)
                                snap.Hand.Add(cards[i]);
                        }
                        var free = st.FreeProps;
                        if (free != null)
                        {
                            for (int i = 0; i < free.Count; i++)
                                snap.FreeProps.Add(free[i]);
                        }
                    }

                    // Fallback: props the round player vo considers usable right now.
                    if (snap.FreeProps.Count == 0)
                    {
                        try
                        {
                            var usable = self.GetFreePropItems();
                            if (usable != null)
                            {
                                for (int i = 0; i < usable.Count; i++)
                                    if (!snap.FreeProps.Contains(usable[i])) snap.FreeProps.Add(usable[i]);
                            }
                        }
                        catch { }
                    }

                    try
                    {
                        var sel = self.PlayerSelectData;
                        if (sel != null) snap.SelectCarId = sel.CarId;
                        if (snap.SelectCarId == 0)
                        {
                            try { snap.SelectCarId = self.GetCurSelectCar(); } catch { }
                        }
                    }
                    catch (Exception ex) { Log.WarnOnce("read SelectData: " + ex.Message); }

                    try
                    {
                        var buffs = self.GetPlayerBuffData();
                        if (buffs != null)
                        {
                            for (int i = 0; i < buffs.Count; i++)
                            {
                                var b = buffs[i];
                                if (b != null) snap.BuffSkillTypes.Add((int)b.SkillType);
                            }
                        }
                    }
                    catch (Exception ex) { Log.WarnOnce("read buffs: " + ex.Message); }

                    var settle = self.PlayerSettle;
                    if (settle != null)
                    {
                        snap.SettleKnown = true;
                        snap.SettleRank = settle.Rank;
                        snap.SettleScore = settle.Score;
                        snap.SettleTotalScore = settle.TotalScore;
                        snap.SettleTotalRank = settle.TotalRank;
                        snap.SettleComplete = settle.Complete;
                    }

                    // Hand limit per car (from the car we picked this round, else the default card limit).
                    snap.HandCardsLimit = CardLimitForCar(snap.SelectCarId);
                }

                LogDebugIfChanged(snap);
            }
            catch (Exception ex)
            {
                snap.Error = ex.Message;
            }
            return snap;
        }

        /// <summary>Card (hand size) limit for a selected car id. Unknown cars fall back to 10.</summary>
        public static uint CardLimitForCar(uint carId)
        {
            switch (carId)
            {
                case GameConfig.CarBeetle: return 8;
                case GameConfig.CarBourbon1500: return 9;
                case GameConfig.CarDickeyZ4: return 10;
                default: return 10;
            }
        }

        // ------------------------------------------------------------------
        // Sending requests (identical to what the game UI calls)
        // ------------------------------------------------------------------

        public static bool SendMatchRequest()
        {
            uint actId = DetectActivityId();
            try { RaceCarModule.API.SendRaceCarMatchRequest(actId); return true; }
            catch (Exception ex) { Log.Warn("MatchRequest failed: " + ex.Message); return false; }
        }

        public static bool SendSelectCarRequest(uint carId)
        {
            uint actId = DetectActivityId();
            try { RaceCarModule.API.SendRaceCarSelectCarRequest(actId, carId); return true; }
            catch (Exception ex) { Log.Warn("SelectCarRequest failed: " + ex.Message); return false; }
        }

        public static bool SendUseCardRequest(IReadOnlyList<uint> cardIds)
        {
            uint actId = DetectActivityId();
            try
            {
                var list = new Il2CppSystem.Collections.Generic.List<uint>();
                for (int i = 0; i < cardIds.Count; i++) list.Add(cardIds[i]);
                RaceCarModule.API.SendRaceCarUseCardRequest(actId, list);
                return true;
            }
            catch (Exception ex) { Log.Warn("UseCardRequest failed: " + ex.Message); return false; }
        }

        public static bool SendUsePropRequest(uint propId, uint index = 0)
        {
            uint actId = DetectActivityId();
            try { RaceCarModule.API.SendRaceCarUsePropRequest(actId, propId, index); return true; }
            catch (Exception ex) { Log.Warn("UsePropRequest failed: " + ex.Message); return false; }
        }

        public static bool SendShuffleRequest()
        {
            uint actId = DetectActivityId();
            try { RaceCarModule.API.SendRaceCarShuffleRequest(actId); return true; }
            catch (Exception ex) { Log.Warn("ShuffleRequest failed: " + ex.Message); return false; }
        }

        // ------------------------------------------------------------------
        // Rank/time helpers
        // ------------------------------------------------------------------

        /// <summary>Self rank within the current round (0 when unknown).</summary>
        public static uint GetSelfRoundRank()
        {
            try
            {
                var cache = AppCache.RaceCar;
                if (cache == null) return 0;
                var room = cache.GetRoomVo(DetectActivityId());
                var roundVo = room?.GetCurRoundVo();
                return roundVo != null ? roundVo.GetSelfRoundRank() : 0;
            }
            catch { return 0; }
        }
    }
}
