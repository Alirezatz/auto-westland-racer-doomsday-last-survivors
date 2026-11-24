namespace WestlandRacerMod
{
    /// <summary>
    /// Runtime tunables, edited live from the in-game panel (F9). Everything the
    /// AI does is opt-in here; the master switch turns the whole brain off.
    ///
    /// Defaults: in-race automation (car selection, card play, prop use) is ON,
    /// so once you are inside a Wasteland Racer race the mod plays it for you.
    /// Auto-entering races is OFF by default because every started match costs a
    /// Racer Pass (item 48515) - flip "Auto match" on in the panel to have the
    /// mod enter races by itself as well.
    /// </summary>
    public sealed class RacerSettings
    {
        // Master switch.
        public bool Enabled = true;

        // --- What to automate ----------------------------------------------
        /// <summary>Send MatchRequest when idle and Auto-match is allowed. Consumes a Racer Pass per match, like the UI button. OFF by default: turn it on in the panel (F9) to have the mod enter races by itself.</summary>
        public bool AutoMatch = false;
        /// <summary>Leave a queued match? (Not wired: there is no cancel button on purpose - use the game UI.)</summary>
        public bool AutoSelectCar = true;
        public bool AutoPlayCards = true;
        public bool AutoUseProps = true;

        /// <summary>Paid props (Card Thief / Card Upgrade / Blind Box / Sync Core / Shuffle) spend Sprint Tokens. OFF by default.</summary>
        public bool UsePaidProps = false;

        // --- Pacing ---------------------------------------------------------
        private float _decisionInterval = 0.5f;
        public float DecisionInterval
        {
            get { return _decisionInterval; }
            set { _decisionInterval = Clamp(value, 0.15f, 2.0f); }
        }

        private float _actionCooldown = 0.6f;
        /// <summary>Minimum seconds between two server requests (keeps the mod polite and human-like).</summary>
        public float ActionCooldown
        {
            get { return _actionCooldown; }
            set { _actionCooldown = Clamp(value, 0.3f, 5.0f); }
        }

        private float _cardCooldown = 5.6f;
        /// <summary>Client-side block between card plays. The server enforces CardCd=5s as well.</summary>
        public float CardCooldown
        {
            get { return _cardCooldown; }
            set { _cardCooldown = Clamp(value, 5.0f, 10.0f); }
        }

        private float _rematchDelay = 5.0f;
        /// <summary>Minimum seconds after a race fully ends before the next MatchRequest is sent.</summary>
        public float RematchDelay
        {
            get { return _rematchDelay; }
            set { _rematchDelay = Clamp(value, 1.0f, 60.0f); }
        }

        // --- Strategy ---------------------------------------------------------
        /// <summary>Homing Missile is only thrown when the current leader is someone else and is at least this far ahead (server units).</summary>
        public long MissileLeadThreshold = 30;

        /// <summary>Road Clearer waits for an actual debuff on self (SpeedDebuff / CardDebuff) before firing.</summary>
        public bool RequireDebuffForRoadClearer = true;

        // --- SmartPlay v2 ---------------------------------------------------
        /// <summary>
        /// The brain thinks before every play: cards are saved once the round is
        /// settled, weak singles are held near supply stations and while we lead
        /// comfortably, dead cards are shed first, and everything is dumped in the
        /// endgame or when we are behind. Disable for legacy "EV dump" behavior.
        /// </summary>
        public bool SmartPlay = true;

        /// <summary>Lead over the second place (server units) that triggers "cruise" mode (skip weak singles).</summary>
        public long CruiseLeadThreshold = 150;
        /// <summary>Remaining distance (units of 2000) within which everything is played, even weak singles.</summary>
        public long EndgameDistance = 300;
        /// <summary>Distance to the next supply station (server units) within which singles are held for the fresh draw.</summary>
        public long StationHoldDistance = 90;
        /// <summary>Minimum gap (units) behind the leader that counts as "losing badly" - triggers all-in mode.</summary>
        public long LosingBadlyGap = 60;
        /// <summary>Skip Coolant Leak: the server answers this prop with errorCode 101 (KEcgeneralInvalidMessage) in this build.</summary>
        public bool UseCoolantLeak = false;

        // --- Cards -------------------------------------------------------------
        /// <summary>Never play more than this many cards in one UseCardRequest (server maximum is 8).</summary>
        public int MaxCardsPerPlay = 8;

        // --- Diagnostics -------------------------------------------------------
        public bool VerboseLogging = true;
        public bool LogPlayDecisions = true;

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
