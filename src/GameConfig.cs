namespace WestlandRacerMod
{
    /// <summary>
    /// Static, verified facts about the Wasteland Racer activity
    /// (ActivityId 191150), taken from the shipped game config:
    ///
    ///   ab/gameconfig_race_car_card_2392363004.ab            (40 unique cards, 4 per point)
    ///   ab/gameconfig_race_car_card_effect_4129665164.ab     (card combos / "Multiple")
    ///   ab/gameconfig_race_car_skill_2655007922.ab          (skill ids -> types)
    ///   ab/gameconfig_race_car_prop_4068863198.ab           (in-race props)
    ///   ab/gameconfig_race_car_supply_station_3763011492.ab (stations at 250/500/750)
    ///   ab/gameconfig_race_car_3809718108.ab                (the 3 race cars)
    ///   ab/gameconfig_race_car_param_1316397353.ab          (global parameters)
    ///
    /// Everything the AI needs at run time is computed from these constants; the
    /// mod never forges packets, it only calls the same public module senders the
    /// game UI calls (see RaceApi).
    /// </summary>
    internal static class GameConfig
    {
        public const uint ActivityId = 191150;

        // ---- Race cars (race_car_3809718108.ab) ---------------------------
        // Highest speed first. Each car is usable in only a limited way, so the
        // AI tracks which ones it already picked this match.
        public const uint CarDickeyZ4 = 10003; // T-Grade, Speed 30300, 10 card limit
        public const uint CarBourbon1500 = 10002; // S-Grade, Speed 30150, 9 card limit
        public const uint CarBeetle = 10001; // A-Grade, Speed 30000, 8 card limit

        public static readonly (uint Id, string Name, uint Speed, int CardLimit, int Grade)[] Cars =
        {
            (CarDickeyZ4,   "Dickey Z4",   30300, 10, 3),
            (CarBourbon1500,"Bourbon 1500",30150,  9, 2),
            (CarBeetle,     "Beetle",      30000,  8, 1),
        };

        // ---- Card deck (race_car_card_2392363004.ab) ----------------------
        // 40 unique card ids, 4 of each point 1..10 (ids 1001..1094).
        public const uint DeckMinId = 1001;
        public const uint DeckMaxId = 1094;

        /// <summary>Card point (face value) for a deck card id. Unknown ids (e.g. upgraded cards) report 10.</summary>
        public static int CardPoint(uint cardId)
        {
            if (cardId >= DeckMinId && cardId <= DeckMaxId)
            {
                // 1001..1004 -> 1, 1011..1014 -> 2, ..., 1091..1094 -> 10
                return (int)((cardId - DeckMinId) / 10) + 1;
            }
            return 10;
        }

        /// <summary>Card score used by the server for one card (higher = stronger).</summary>
        public static int CardScore(uint cardId)
        {
            switch (CardPoint(cardId))
            {
                case 1: return 150;
                case 2: return 160;
                case 3: return 170;
                case 4: return 180;
                case 5: return 190;
                case 6: return 200;
                case 7: return 205;
                case 8: return 210;
                case 9: return 215;
                default: return 220;
            }
        }

        // ---- Card combos (race_car_card_effect_4129665164.ab) --------------
        // Combo id -> (cards required, Multiple, UI bonus %, English name).
        public const int ComboSingle = 1;
        public const int ComboPair = 2;
        public const int Combo2PairRun = 3;
        public const int Combo3PairRun = 4;
        public const int Combo4PairRun = 5;
        public const int ComboTriple = 6;
        public const int ComboTripleRun = 7;
        public const int ComboRun4 = 8;
        public const int ComboRun5 = 9;
        public const int ComboRun6 = 10;
        public const int ComboRun7 = 11;
        public const int ComboRun8 = 12;
        public const int ComboQuad = 13;

        public readonly struct ComboDef
        {
            public readonly int Id;
            public readonly int Multiple;   // raw Multiple from the config table
            public readonly int BonusPct;   // "CombinationDescKey" bonus % shown in the UI
            public readonly int CardsUsed;
            public readonly string Name;

            public ComboDef(int id, int multiple, int bonusPct, int cardsUsed, string name)
            {
                Id = id; Multiple = multiple; BonusPct = bonusPct; CardsUsed = cardsUsed; Name = name;
            }
        }

        public static readonly ComboDef[] Combos =
        {
            new ComboDef(ComboSingle,     0,   0, 1, "Single"),
            new ComboDef(ComboPair,       500, 150, 2, "Pair"),
            new ComboDef(Combo2PairRun,   2000, 250, 4, "2-Pair Run"),
            new ComboDef(Combo3PairRun,   3500, 420, 6, "3-Pair Run"),
            new ComboDef(Combo4PairRun,   4300, 500, 8, "4-Pair Run"),
            new ComboDef(ComboTriple,     2200, 300, 3, "Triple"),
            new ComboDef(ComboTripleRun,  4300, 500, 6, "Triple Run"),
            new ComboDef(ComboRun4,        600, 140, 4, "4-Card Run"),
            new ComboDef(ComboRun5,       1800, 240, 5, "5-Card Run"),
            new ComboDef(ComboRun6,       2800, 350, 6, "6-Card Run"),
            new ComboDef(ComboRun7,       3200, 390, 7, "7-Card Run"),
            new ComboDef(ComboRun8,       3500, 420, 8, "8-Card Run"),
            new ComboDef(ComboQuad,       5300, 600, 4, "Quad"),
        };

        public static ComboDef ComboById(int id)
        {
            for (int i = 0; i < Combos.Length; i++)
                if (Combos[i].Id == id) return Combos[i];
            return Combos[0];
        }

        /// <summary>
        /// Skill types as used by the wire protocol ("RaceCarSkillType" in
        /// dls.message.dll) / skill config Type column.
        /// </summary>
        public const int SkillTypeShuffle = 1;
        public const int SkillTypeSpeedBuff = 2;
        public const int SkillTypeSpeedDebuff = 3;
        public const int SkillTypeCardBuff = 4;
        public const int SkillTypeCardDebuff = 5;
        public const int SkillTypeShieldBuff = 6;
        public const int SkillTypeCardGain = 7;
        public const int SkillTypeClearDebuff = 8;
        public const int SkillTypeCardRob = 9;
        public const int SkillTypeCardUp = 10;
        public const int SkillTypePropGain = 11;
        public const int SkillTypeCardShareBuff = 12;

        // ---- Props (race_car_prop_4068863198.ab) ---------------------------
        public const uint PropShuffle = 48501;        // Shuffle (5 Sprint Tokens)
        public const uint PropHighOctaneFuel = 48502; // +200 speed, 2s
        public const uint PropSludgeOil = 48503;      // all others -100, 1s
        public const uint PropHomingMissile = 48504;  // leader -150, 2s
        public const uint PropPowerCanister = 48505;  // +200 card effect, 10s
        public const uint PropCoolantLeak = 48506;    // all others -100 card effect, 6s
        public const uint PropIonShield = 48507;      // blocks one slowing effect
        public const uint PropCardAce = 48508;        // +2 random cards
        public const uint PropRoadClearer = 48509;    // remove bad effects
        public const uint PropCardThief = 48510;      // steal 1 card (5 Sprint Tokens)
        public const uint PropCardUpgrade = 48511;    // +2 value to 2 cards (5 Sprint Tokens)
        public const uint PropBlindBox = 48512;       // 1-2 random items (5 Sprint Tokens)
        public const uint PropSyncCore = 48513;       // share speed buffs (5 Sprint Tokens)

        public const uint SprintToken = 48514;   // in-event currency
        public const uint RacerPass = 48515;     // entry ticket

        public static string PropName(uint propId)
        {
            switch (propId)
            {
                case PropShuffle: return "Shuffle";
                case PropHighOctaneFuel: return "High-Octane Fuel";
                case PropSludgeOil: return "Sludge Oil";
                case PropHomingMissile: return "Homing Missile";
                case PropPowerCanister: return "Power Canister";
                case PropCoolantLeak: return "Coolant Leak";
                case PropIonShield: return "Ion Shield";
                case PropCardAce: return "Card Ace";
                case PropRoadClearer: return "Road Clearer";
                case PropCardThief: return "Card Thief";
                case PropCardUpgrade: return "Card Upgrade";
                case PropBlindBox: return "Blind Box";
                case PropSyncCore: return "Sync Core";
                default: return "Prop #" + propId;
            }
        }

        /// <summary>Props the paid path may use (cost Sprint Tokens). Never used unless the user opts in.</summary>
        public static bool IsPaidProp(uint propId)
        {
            return propId == PropCardThief || propId == PropCardUpgrade ||
                   propId == PropBlindBox || propId == PropSyncCore ||
                   propId == PropShuffle;
        }
    }
}
