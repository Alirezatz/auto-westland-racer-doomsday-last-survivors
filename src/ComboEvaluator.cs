using System;
using System.Collections.Generic;

namespace WestlandRacerMod
{
    /// <summary>
    /// Local implementation of the Wasteland Racer card system.
    ///
    /// Two hard facts shaped this class:
    ///
    ///  1) The server is authoritative for speed, but cards are played by the
    ///     client. Empirically (live-fire logs, 2026-11-18) the server rejects
    ///     arbitrary card *supersets* with errorCode 101 (KEcgeneralInvalidMessage),
    ///     while the game UI validates selections with
    ///     "Card combination error. Please reselect." (message 41052) and the
    ///     config ships an exact pattern list for every legal effect in
    ///     race_car_card_effect_*.ab. Conclusion: a legal play IS one of the
    ///     configured combinations (singles included) - nothing more.
    ///
    ///  2) The 40-card deck is fixed (4 copies of points 1..10, unique ids
    ///     1001..1094), so it is cheap to enumerate every legal combination
    ///     present in the hand.
    ///
    /// The AI therefore plays the legal combination with the highest expected
    /// value:   value = sum(points) * (1 + bonus/100) + 0.05 * count
    /// where the bonus is the UI "CombinationDescKey" percentage of the combo.
    /// Singles are always playable, so any non-empty hand yields a decision.
    /// </summary>
    internal static class ComboEvaluator
    {
        public readonly struct PlayDecision
        {
            public readonly List<uint> CardIds;
            public readonly List<int> Points;
            public readonly int SumPoints;
            public readonly GameConfig.ComboDef BestCombo;
            public readonly double ExpectedValue;

            public PlayDecision(List<uint> cardIds, List<int> points, int sumPoints,
                GameConfig.ComboDef bestCombo, double expectedValue)
            {
                CardIds = cardIds;
                Points = points;
                SumPoints = sumPoints;
                BestCombo = bestCombo;
                ExpectedValue = expectedValue;
            }

            public override string ToString()
            {
                return BestCombo.Name + " (" + string.Join(",", Points) + ") sum=" + SumPoints
                       + " bonus=+" + BestCombo.BonusPct + "% ev=" + ExpectedValue.ToString("F1");
            }
        }

        private readonly struct Candidate
        {
            public readonly List<uint> Ids;
            public readonly List<int> Points;
            public readonly GameConfig.ComboDef Combo;
            public readonly int SumPoints;
            public readonly double Value;

            public Candidate(List<uint> ids, List<int> points, GameConfig.ComboDef combo)
            {
                Ids = ids;
                Points = points;
                Combo = combo;
                SumPoints = 0;
                for (int i = 0; i < points.Count; i++) SumPoints += points[i];
                Value = SumPoints * (1.0 + combo.BonusPct / 100.0) + 0.05 * ids.Count;
            }
        }

        /// <summary>
        /// The strongest combination that is a SUBSET of the given points.
        /// Kept for rank/display purposes; gameplay uses LegalPlays directly.
        /// </summary>
        public static GameConfig.ComboDef BestCombo(IReadOnlyList<int> points)
        {
            if (points == null || points.Count == 0)
                return GameConfig.ComboById(GameConfig.ComboSingle);

            int maxRank = 12;
            int[] count = new int[maxRank + 1];
            for (int i = 0; i < points.Count; i++)
            {
                int p = points[i];
                if (p >= 1 && p <= maxRank) count[p]++;
            }

            GameConfig.ComboDef best = GameConfig.ComboById(GameConfig.ComboSingle);

            int maxOfAKind = 0;
            for (int r = 1; r <= maxRank; r++)
                if (count[r] > maxOfAKind) maxOfAKind = count[r];
            if (maxOfAKind >= 4) best = Higher(best, GameConfig.ComboById(GameConfig.ComboQuad));
            if (maxOfAKind >= 3) best = Higher(best, GameConfig.ComboById(GameConfig.ComboTriple));
            if (maxOfAKind >= 2) best = Higher(best, GameConfig.ComboById(GameConfig.ComboPair));

            for (int r = 1; r < maxRank; r++)
                if (count[r] >= 3 && count[r + 1] >= 3)
                {
                    best = Higher(best, GameConfig.ComboById(GameConfig.ComboTripleRun));
                    break;
                }

            int pairRun = 0;
            for (int r = 1; r <= maxRank; r++)
            {
                if (count[r] >= 2)
                {
                    pairRun++;
                    if (pairRun == 2) best = Higher(best, GameConfig.ComboById(GameConfig.Combo2PairRun));
                    else if (pairRun == 3) best = Higher(best, GameConfig.ComboById(GameConfig.Combo3PairRun));
                    else if (pairRun >= 4) best = Higher(best, GameConfig.ComboById(GameConfig.Combo4PairRun));
                }
                else pairRun = 0;
            }

            int straight = 0;
            for (int r = 1; r <= maxRank; r++)
            {
                if (count[r] > 0)
                {
                    straight++;
                    if (straight == 4) best = Higher(best, GameConfig.ComboById(GameConfig.ComboRun4));
                    else if (straight == 5) best = Higher(best, GameConfig.ComboById(GameConfig.ComboRun5));
                    else if (straight == 6) best = Higher(best, GameConfig.ComboById(GameConfig.ComboRun6));
                    else if (straight == 7) best = Higher(best, GameConfig.ComboById(GameConfig.ComboRun7));
                    else if (straight >= 8) best = Higher(best, GameConfig.ComboById(GameConfig.ComboRun8));
                }
                else straight = 0;
            }

            return best;
        }
        private static GameConfig.ComboDef Higher(GameConfig.ComboDef a, GameConfig.ComboDef b)
        {
            return b.Multiple > a.Multiple ? b : a;
        }

        /// <summary>
        /// Enumerate every LEGAL play in the hand (each one exactly one of the
        /// configured effect patterns, or a single card), and yield them ordered
        /// from best to worst by expected value. Singles are always present.
        /// </summary>
        public static List<PlayDecision> LegalPlays(IReadOnlyList<uint> hand, int maxCards = 8)
        {
            var result = new List<PlayDecision>();
            if (hand == null || hand.Count == 0) return result;

            if (maxCards < 1) maxCards = 1;
            if (maxCards > 8) maxCards = 8;

            // Group hand by point, keeping (id, point) pairs.
            var byPoint = new Dictionary<int, List<uint>>();
            for (int i = 0; i < hand.Count; i++)
            {
                int p = GameConfig.CardPoint(hand[i]);
                List<uint> list;
                if (!byPoint.TryGetValue(p, out list)) { list = new List<uint>(); byPoint[p] = list; }
                list.Add(hand[i]);
            }

            var candidates = new List<Candidate>();

            // --- Singles (always legal) --------------------------------------
            foreach (var kv in byPoint)
                foreach (var id in kv.Value)
                    candidates.Add(Make(new List<uint> { id }, new List<int> { kv.Key }, GameConfig.ComboSingle));

            int CountOf(int p) => byPoint.TryGetValue(p, out var l) ? l.Count : 0;

            List<uint> IdsOf(int p, int take, int skip = 0)
            {
                var ids = new List<uint>();
                var l = byPoint[p];
                for (int i = skip; i < l.Count && ids.Count < take; i++) ids.Add(l[i]);
                return ids;
            }

            // --- Pair / Triple / Quad -----------------------------------------
            foreach (var kv in byPoint)
            {
                int p = kv.Key;
                int c = kv.Value.Count;
                if (c >= 2) candidates.Add(Make(IdsOf(p, 2), Pts(p, 2), GameConfig.ComboPair));
                if (c >= 3) candidates.Add(Make(IdsOf(p, 3), Pts(p, 3), GameConfig.ComboTriple));
                if (c >= 4) candidates.Add(Make(IdsOf(p, 4), Pts(p, 4), GameConfig.ComboQuad));
            }

            // --- Triple Run: two consecutive tripled ranks ---------------------
            for (int p = 1; p <= 10; p++)
            {
                if (CountOf(p) >= 3 && CountOf(p + 1) >= 3)
                {
                    var ids = IdsOf(p, 3); ids.AddRange(IdsOf(p + 1, 3));
                    var pts = Pts(p, 3); pts.AddRange(Pts(p + 1, 3));
                    candidates.Add(Make(ids, pts, GameConfig.ComboTripleRun));
                }
            }

            // --- Consecutive pair-runs (2/3/4-Pair Run) -------------------------
            foreach (var pairRunStart in ConsecutiveRuns(CountOf, 2, 2))
            {
                int len = pairRunStart.Item2;
                if (len > 4) len = 4; // only 2/3/4-Pair Run are configured effects
                int start = pairRunStart.Item1;
                var ids = new List<uint>(); var pts = new List<int>();
                for (int p = start; p < start + len; p++)
                {
                    ids.AddRange(IdsOf(p, 2));
                    pts.AddRange(Pts(p, 2));
                }
                int comboId = len == 2 ? GameConfig.Combo2PairRun : (len == 3 ? GameConfig.Combo3PairRun : GameConfig.Combo4PairRun);
                candidates.Add(Make(ids, pts, comboId));
            }

            // --- Straights (4..8 distinct consecutive ranks) ---------------------
            foreach (var straight in ConsecutiveRuns(CountOf, 1, 4))
            {
                int start = straight.Item1;
                int len = Math.Min(straight.Item2, 8);
                for (int L = 4; L <= len; L++)
                {
                    var ids = new List<uint>(); var pts = new List<int>();
                    for (int p = start; p < start + L; p++)
                    {
                        ids.Add(byPoint[p][0]); // one copy per rank is enough
                        pts.Add(p);
                    }
                    int comboId = L == 4 ? GameConfig.ComboRun4 : (L == 5 ? GameConfig.ComboRun5 : (L == 6 ? GameConfig.ComboRun6 : (L == 7 ? GameConfig.ComboRun7 : GameConfig.ComboRun8)));
                    candidates.Add(Make(ids, pts, comboId));
                }
            }

            // --- Keep only legal plays within the card cap -----------------------
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.Ids.Count > maxCards) continue;
                result.Add(new PlayDecision(c.Ids, c.Points, c.SumPoints, c.Combo, c.Value));
            }

            result.Sort((a, b) => b.ExpectedValue.CompareTo(a.ExpectedValue));
            return result;
        }

        private static Candidate Make(List<uint> ids, List<int> pts, int comboId)
        {
            return new Candidate(ids, pts, GameConfig.ComboById(comboId));
        }

        private static List<int> Pts(int point, int count)
        {
            var pts = new List<int>(count);
            for (int i = 0; i < count; i++) pts.Add(point);
            return pts;
        }

        /// <summary>Find consecutive runs of ranks 1..10 where count >= minCount. Yields (start, length).</summary>
        private static IEnumerable<(int, int)> ConsecutiveRuns(Func<int, int> countOf, int minCount, int minLength)
        {
            for (int start = 1; start <= 10; start++)
            {
                if (countOf(start) < minCount) continue;
                int len = 0;
                while (countOf(start + len) >= minCount && start + len <= 10) len++;
                if (len >= minLength)
                {
                    if (minCount == 1 || minCount == 2)
                    {
                        // emit for every length >= minLength
                        for (int L = minLength; L <= len; L++) yield return (start, L);
                    }
                    else
                    {
                        yield return (start, len);
                    }
                }
            }
        }

        /// <summary>
        /// SmartPlay v2:
        /// For every card point that appears in the hand, score how much that card
        /// is worth KEEPING. Low score = dead card (sheds nothing into combos).
        ///   pairs: every extra copy beyond the first -> +2
        ///   runs:  every distinct consecutive window of length >= 3 that is fully
        ///          present in the hand and contains the point -> +1
        /// </summary>
        public static int KeepScore(int point, IReadOnlyDictionary<int, List<uint>> byPoint)
        {
            int copies = byPoint.TryGetValue(point, out var l) ? l.Count : 0;
            int score = (copies - 1) * 2;

            for (int start = Math.Max(1, point - 7); start <= point; start++)
            {
                for (int len = 3; len <= 8; len++)
                {
                    int end = start + len - 1;
                    if (end > 10) break;
                    if (point < start || point > end) continue;
                    bool full = true;
                    for (int p = start; p <= end; p++)
                    {
                        if (!byPoint.TryGetValue(p, out var el) || el.Count == 0) { full = false; break; }
                    }
                    if (full) score += 1;
                }
            }
            return score;
        }

        /// <summary>
        /// The least useful card for shedding: lowest keep-score, tie-break highest
        /// point. Returns 0 for an empty hand.
        /// </summary>
        public static uint PickShedCard(IReadOnlyList<uint> hand)
        {
            if (hand == null || hand.Count == 0) return 0;

            var byPoint = new Dictionary<int, List<uint>>();
            for (int i = 0; i < hand.Count; i++)
            {
                int p = GameConfig.CardPoint(hand[i]);
                List<uint> list;
                if (!byPoint.TryGetValue(p, out list)) { list = new List<uint>(); byPoint[p] = list; }
                list.Add(hand[i]);
            }

            uint bestId = hand[0];
            int bestScore = int.MaxValue;
            int bestPoint = -1;
            for (int i = 0; i < hand.Count; i++)
            {
                int p = GameConfig.CardPoint(hand[i]);
                int keep = KeepScore(p, byPoint);
                if (keep < bestScore || (keep == bestScore && p > bestPoint))
                {
                    bestScore = keep;
                    bestPoint = p;
                    bestId = hand[i];
                }
            }
            return bestId;
        }

        /// <summary>
        /// Tells whether the hand holds at least one legal combo (any pair+).
        /// </summary>
        public static bool HasComboPotential(IReadOnlyList<uint> hand)
        {
            var plays = LegalPlays(hand, 8);
            for (int i = 0; i < plays.Count; i++)
                if (plays[i].BestCombo.Id != GameConfig.ComboSingle) return true;
            return false;
        }

        /// <summary>
        /// Pick the best legal play. When the best play is a single and the hand
        /// holds a better shed candidate (a dead card that scores nothing into
        /// future combos), the single is swapped to that card: shedding what we
        /// can never use beats playing the highest point that breaks a build.
        /// </summary>
        public static PlayDecision Decide(IReadOnlyList<uint> hand, int maxCards = 8)
        {
            var plays = LegalPlays(hand, maxCards);
            if (plays.Count == 0) return default;

            // Shed vote: if the plan is a bare single, prefer the least useful card.
            if (plays[0].BestCombo.Id == GameConfig.ComboSingle && hand.Count > 1)
            {
                uint shed = PickShedCard(hand);
                if (shed != 0 && shed != plays[0].CardIds[0])
                {
                    return new PlayDecision(
                        new List<uint> { shed },
                        new List<int> { GameConfig.CardPoint(shed) },
                        GameConfig.CardPoint(shed),
                        GameConfig.ComboById(GameConfig.ComboSingle),
                        GameConfig.CardPoint(shed) * 1.0 + 0.05);
                }
            }
            return plays[0];
        }

        /// <summary>Human readable summary of the current hand for the overlay.</summary>
        public static string DescribeHand(IReadOnlyList<uint> hand)
        {
            if (hand == null || hand.Count == 0) return "hand: empty";
            int[] points = new int[hand.Count];
            for (int i = 0; i < hand.Count; i++) points[i] = GameConfig.CardPoint(hand[i]);
            Array.Sort(points);
            return "hand(" + hand.Count + "): [" + string.Join(",", points) + "]";
        }

    }
}
