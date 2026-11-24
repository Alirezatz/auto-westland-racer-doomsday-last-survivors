using UnityEngine;

namespace WestlandRacerMod
{
    /// <summary>In-game overlay (F9). Mirrors the style of the other doomsday mods.</summary>
    internal sealed class RacerUi
    {
        private const float X = 20f;
        private const float Y = 20f;
        private const float Width = 380f;
        private const float Height = 474f;

        private static readonly Color Background = new Color(0.07f, 0.08f, 0.10f, 0.95f);
        private static readonly Color ButtonIdle = new Color(0.20f, 0.22f, 0.28f, 1f);
        private static readonly Color ButtonOn = new Color(0.20f, 0.55f, 0.26f, 1f);
        private static readonly Color ButtonWarn = new Color(0.55f, 0.33f, 0.10f, 1f);
        private static readonly Color ButtonGold = new Color(0.55f, 0.47f, 0.13f, 1f);
        private static readonly Color TextDim = new Color(0.75f, 0.75f, 0.78f, 1f);
        private static readonly Color TextHot = new Color(1f, 0.85f, 0.35f, 1f);

        public bool Visible = true;
        public KeyCode ToggleKey = KeyCode.F9;

        private string _cachedRank = "-";
        private float _rankCachedAt = -10f;
        private const float RankCacheSeconds = 2f;

        private static readonly GUIStyle DimStyle;
        private static readonly GUIStyle HotStyle;

        static RacerUi()
        {
            DimStyle = new GUIStyle();
            DimStyle.normal.textColor = TextDim;
            DimStyle.fontSize = 11;
            HotStyle = new GUIStyle();
            HotStyle.normal.textColor = TextHot;
            HotStyle.fontSize = 11;
        }

        public void Draw(RacerSettings settings, RacerBrain brain)
        {
            if (!Visible) return;

            GUI.color = Color.white;
            GUI.backgroundColor = Background;
            GUI.Box(new Rect(X, Y, Width, Height), "");

            GUI.Label(new Rect(X + 14f, Y + 8f, Width - 28f, 20f),
                "Wasteland Racer AI  v1.1.0");

            settings.Enabled = Checkbox(X + 14f, Y + 30f, Width - 28f, 24f,
                settings.Enabled, "AI-Play (master)", ButtonOn);

            // Auto-match row is emphasised because it consumes Racer Passes.
            bool prevAutoMatch = settings.AutoMatch;
            settings.AutoMatch = Checkbox(X + 14f, Y + 58f, Width - 28f, 24f,
                settings.AutoMatch, "Auto match (uses Racer Passes!)", ButtonWarn);
            if (prevAutoMatch != settings.AutoMatch)
                Log.Info("Auto match set to " + settings.AutoMatch + " from the panel.");

            settings.AutoSelectCar = Checkbox(X + 14f, Y + 86f, Width - 28f, 22f,
                settings.AutoSelectCar, "Auto select best vehicle", ButtonOn);
            settings.AutoPlayCards = Checkbox(X + 14f, Y + 110f, Width - 28f, 22f,
                settings.AutoPlayCards, "Auto play cards (best combo)", ButtonOn);
            settings.AutoUseProps = Checkbox(X + 14f, Y + 134f, Width - 28f, 22f,
                settings.AutoUseProps, "Auto use props", ButtonOn);
            settings.SmartPlay = Checkbox(X + 14f, Y + 158f, Width - 28f, 22f,
                settings.SmartPlay, "Smart play (hold weak singles)", ButtonOn);
            settings.UsePaidProps = Checkbox(X + 14f, Y + 182f, Width - 28f, 22f,
                settings.UsePaidProps, "Use paid props (Sprint Tokens)", ButtonGold);

            float sy = Y + 210f;
            GUI.Label(new Rect(X + 14f, sy, Width - 28f, 16f),
                "--- status ---", DimStyle);

            var snap = brain.LastSnapshot;
            string statusLine = "phase: " + brain.CurrentPhase + "   act: " + (snap != null ? snap.ActivityId.ToString() : "-")
                + "   round: " + (snap != null && snap.RoomFound ? snap.CurRound.ToString() + "/3" : "-")
                + "   stage: " + StageName(snap != null ? snap.Stage : -1)
                + "   mode: " + brain.LastModeText;
            GUI.Label(new Rect(X + 14f, sy + 16f, Width - 28f, 18f), statusLine);

            if (Time.time - _rankCachedAt >= RankCacheSeconds)
            {
                _rankCachedAt = Time.time;
                try { _cachedRank = RaceApi.GetSelfRoundRank().ToString(); }
                catch { _cachedRank = "-"; }
            }

            string posLine = snap != null && snap.SelfFound
                ? ("pos " + snap.SelfPosition + "/2000   speed " + snap.SelfSpeed
                   + "   hand " + snap.Hand.Count + "/" + snap.HandCardsLimit
                   + "   props [" + PropList(snap) + "]"
                   + "   rank " + _cachedRank)
                : "not inside a race right now";
            GUI.Label(new Rect(X + 14f, sy + 34f, Width - 28f, 18f), posLine);

            string carLine = (snap != null && snap.SelectCarId != 0)
                ? "car: " + CarLabel(snap.SelectCarId)
                : "car: not selected";
            GUI.Label(new Rect(X + 14f, sy + 52f, Width - 28f, 18f), carLine);

            string combo = brain.LastComboText;
            if (!string.IsNullOrEmpty(combo))
                GUI.Label(new Rect(X + 14f, sy + 70f, Width - 28f, 18f),
                    "last play: " + combo, HotStyle);
            else if (snap != null && snap.SelfFound)
                GUI.Label(new Rect(X + 14f, sy + 70f, Width - 28f, 18f),
                    ComboEvaluator.DescribeHand(snap.Hand));

            if (snap != null && !string.IsNullOrEmpty(snap.Error))
                GUI.Label(new Rect(X + 14f, sy + 88f, Width - 28f, 18f),
                    "warning: " + snap.Error);

            float ty = sy + 108f;
            GUI.Label(new Rect(X + 14f, ty, Width - 28f, 16f), "--- session ---", DimStyle);
            var st = brain.Stats;
            string stats1 = "matches " + st.MatchesStarted
                + "   races finished " + st.RacesFinished
                + "   round wins " + st.RoundWins
                + "   avg final rank " + st.AverageRank();
            GUI.Label(new Rect(X + 14f, ty + 16f, Width - 28f, 18f), stats1);
            string stats2 = "cards played " + st.CardsPlayed
                + "   biggest play " + st.BiggestPlay
                + "   best combo " + st.BestComboName;
            GUI.Label(new Rect(X + 14f, ty + 34f, Width - 28f, 18f), stats2);
            string stats3 = "props used " + st.PropsUsed
                + "   singles held " + brain.HeldSinglesCount;
            GUI.Label(new Rect(X + 14f, ty + 52f, Width - 28f, 18f), stats3);
            GUI.Label(new Rect(X + 14f, ty + 70f, Width - 28f, 18f),
                brain.MatchRequested ? "waiting for matchmaking..." : (brain.LastActionText ?? ""),
                DimStyle);

            GUI.backgroundColor = Color.white;
        }

        private static string PropList(RaceSnapshot snap)
        {
            if (snap.FreeProps.Count == 0) return "-";
            var parts = new System.Collections.Generic.List<string>(snap.FreeProps.Count);
            for (int i = 0; i < snap.FreeProps.Count; i++)
                parts.Add(GameConfig.PropName(snap.FreeProps[i]));
            return string.Join(",", parts);
        }

        private static string CarLabel(uint carId)
        {
            string name = "?";
            for (int i = 0; i < GameConfig.Cars.Length; i++)
                if (GameConfig.Cars[i].Id == carId) name = GameConfig.Cars[i].Name;
            return name + " (" + RacerBrain.CarGrade(carId) + "-grade)";
        }

        private static string StageName(int stage)
        {
            switch (stage)
            {
                case 0: return "Select";
                case 1: return "Enter";
                case 2: return "Ready";
                case 3: return "Game";
                case 4: return "Settle";
                case 5: return "End";
                default: return "-";
            }
        }

        private bool Checkbox(float x, float y, float width, float height, bool value, string label, Color onColor)
        {
            GUI.backgroundColor = value ? onColor : ButtonIdle;
            bool clicked = GUI.Button(new Rect(x, y, width, height),
                value ? "[X] " + label + "  ON" : "[ ] " + label + "  OFF");
            GUI.backgroundColor = Color.white;
            return clicked ? !value : value;
        }
    }
}
