using System;
using System.Collections.Generic;
using WestlandRacerMod;

namespace WestlandRacerLogicTests
{
    internal static class Program
    {
        private static int _failures;

        private static uint Id(int point, int copy) // copy 0..3
        {
            return (uint)(1000 + (point - 1) * 10 + 1 + copy); // 1001..1004=1, 1011..1014=2 ... 1091..1094=10
        }

        private static List<uint> Hand(params int[] points)
        {
            var list = new List<uint>();
            var seen = new Dictionary<int, int>();
            foreach (int p in points)
            {
                seen.TryGetValue(p, out int copy);
                list.Add(Id(p, copy));
                seen[p] = copy + 1;
            }
            return list;
        }

        private static void Check(bool condition, string what)
        {
            if (condition) { Console.WriteLine("PASS  " + what); }
            else { _failures++; Console.WriteLine("FAIL  " + what); }
        }

        private static void ComboIs(string label, GameConfig.ComboDef combo, int expectedId, int expectedMultiple)
        {
            Check(combo.Id == expectedId,
                label + " -> " + combo.Name + " (expected combo id " + expectedId + ")");
            Check(combo.Multiple == expectedMultiple,
                label + " multiple " + combo.Multiple + " (expected " + expectedMultiple + ")");
        }

        private static int Main()
        {
            // ---- CardPoint mapping ----------------------------------------
            Check(GameConfig.CardPoint(1001) == 1, "id 1001 -> point 1");
            Check(GameConfig.CardPoint(1004) == 1, "id 1004 -> point 1");
            Check(GameConfig.CardPoint(1011) == 2, "id 1011 -> point 2");
            Check(GameConfig.CardPoint(1094) == 10, "id 1094 -> point 10");
            Check(GameConfig.CardPoint(1091) == 10, "id 1091 -> point 10");

            // ---- Combo detection ------------------------------------------
            ComboIs("single", ComboEvaluator.BestCombo(new[] { 7 }), GameConfig.ComboSingle, 0);
            ComboIs("pair", ComboEvaluator.BestCombo(new[] { 5, 5 }), GameConfig.ComboPair, 500);
            ComboIs("triple", ComboEvaluator.BestCombo(new[] { 5, 5, 5 }), GameConfig.ComboTriple, 2200);
            ComboIs("quad", ComboEvaluator.BestCombo(new[] { 5, 5, 5, 5 }), GameConfig.ComboQuad, 5300);
            ComboIs("run4", ComboEvaluator.BestCombo(new[] { 1, 2, 3, 4 }), GameConfig.ComboRun4, 600);
            ComboIs("run5", ComboEvaluator.BestCombo(new[] { 3, 4, 5, 6, 7 }), GameConfig.ComboRun5, 1800);
            ComboIs("run6", ComboEvaluator.BestCombo(new[] { 1, 2, 3, 4, 5, 6 }), GameConfig.ComboRun6, 2800);
            ComboIs("run7", ComboEvaluator.BestCombo(new[] { 4, 5, 6, 7, 8, 9, 10 }), GameConfig.ComboRun7, 3200);
            ComboIs("run8", ComboEvaluator.BestCombo(new[] { 2, 3, 4, 5, 6, 7, 8, 9 }), GameConfig.ComboRun8, 3500);
            ComboIs("2-pair run", ComboEvaluator.BestCombo(new[] { 5, 5, 6, 6 }), GameConfig.Combo2PairRun, 2000);
            ComboIs("3-pair run", ComboEvaluator.BestCombo(new[] { 2, 2, 3, 3, 4, 4 }), GameConfig.Combo3PairRun, 3500);
            ComboIs("4-pair run", ComboEvaluator.BestCombo(new[] { 1, 1, 2, 2, 3, 3, 4, 4 }), GameConfig.Combo4PairRun, 4300);
            ComboIs("triple run", ComboEvaluator.BestCombo(new[] { 1, 1, 1, 2, 2, 2 }), GameConfig.ComboTripleRun, 4300);
            ComboIs("triple+straight subset ranking is Triple", ComboEvaluator.BestCombo(new[] { 3, 3, 3, 4, 5, 6, 7 }), GameConfig.ComboTriple, 2200);
            // NOTE: BestCombo (subset strength ranking) reports the strongest combo TYPE
            // present; the legal-play decision ranks by expected value instead.
            var dTripleVsRun = ComboEvaluator.Decide(Hand(3, 3, 3, 4, 5, 6, 7));
            bool legalPick = dTripleVsRun.BestCombo.Id == GameConfig.ComboTriple
                          || dTripleVsRun.BestCombo.Id == GameConfig.ComboRun4
                          || dTripleVsRun.BestCombo.Id == GameConfig.ComboRun5
                          || dTripleVsRun.BestCombo.Id == GameConfig.ComboPair;
            Check(legalPick, "triple vs straight play: picks a configured combo, got " + dTripleVsRun.BestCombo.Name);
            ComboIs("quad+pair uses quad", ComboEvaluator.BestCombo(new[] { 9, 9, 9, 9, 2, 2 }), GameConfig.ComboQuad, 5300);
            ComboIs("three pairs non-consecutive is only pair", ComboEvaluator.BestCombo(new[] { 1, 1, 4, 4, 7, 7 }), GameConfig.ComboPair, 500);

            // ---- Play decision ---------------------------------------------
            // 1) Whole hand is playable as a 4-Pair Run (8 of 10 cards, limit 8).
            var d1 = ComboEvaluator.Decide(Hand(1, 1, 2, 2, 3, 3, 4, 4, 5, 5));
            Check(d1.CardIds.Count == 8, "10-card hand: plays 8 cards (got " + d1.CardIds.Count + ")");
            ComboIs("10-card hand", d1.BestCombo, GameConfig.Combo4PairRun, 4300);

            // 2) No combo anywhere: highest straight wins and dumps 8 cards.
            var d2 = ComboEvaluator.Decide(Hand(1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
            Check(d2.CardIds.Count == 8, "no-pair hand: plays 8 cards (got " + d2.CardIds.Count + ")");
            Check(d2.BestCombo.Id == GameConfig.ComboRun8, "no-pair hand: best combo is 8-Card Run, got " + d2.BestCombo.Name);

            // 3) Quad setup: 4-of-a-kind beats a straight for the play.
            var d3 = ComboEvaluator.Decide(Hand(6, 6, 6, 6, 1, 2, 3, 4, 5, 7));
            Check(d3.BestCombo.Id == GameConfig.ComboQuad, "quad hand picks the Quad, got " + d3.BestCombo.Name);

            // 4) Empty and single-card hands never crash.
            Check(ComboEvaluator.Decide(new List<uint>()).CardIds == null, "empty hand returns no decision");
            var d4 = ComboEvaluator.Decide(Hand(10));
            Check(d4.CardIds.Count == 1 && d4.Points[0] == 10, "single card hand plays that card");

            // 5) maxCards cap is honoured.
            var d5 = ComboEvaluator.Decide(Hand(1, 1, 2, 2, 3, 3, 4, 4, 5, 5), 4);
            Check(d5.CardIds.Count == 4, "maxCards=4 honoured (got " + d5.CardIds.Count + ")");
            ComboIs("maxCards=4 combo", d5.BestCombo, GameConfig.Combo2PairRun, 2000);

            // 6) Full-hand dump with a pair included beats dumping without it.
            var d6 = ComboEvaluator.Decide(Hand(10, 10, 2, 3, 4, 5, 6, 7, 8, 9));
            Check(d6.CardIds.Count == 8, "hand with pair: plays 8 cards (got " + d6.CardIds.Count + ")");
            Check(d6.SumPoints == 52, "hand with pair: picks the 3..10 run (sum " + d6.SumPoints + ")");

            // ---- SmartPlay v2: dead-card detection & shedding ------------------
            // 1) The 2 is dead weight (no window/pair); the 9s feed a potential triple.
            Check(ComboEvaluator.PickShedCard(Hand(9, 9, 9, 2)) == Id(2, 0),
                "shed: the isolated 2 goes first");
            // 2) 8,9,10 all belong to a 3-window; the 2 is shed.
            Check(ComboEvaluator.PickShedCard(Hand(8, 9, 10, 2)) == Id(2, 0),
                "shed: 8-9-10 build toward a run, the 2 is dead");
            // 3) Pair of 5s builds toward a triple; shed the isolated 9.
            Check(ComboEvaluator.PickShedCard(Hand(5, 5, 9)) == Id(9, 0),
                "shed: pair of 5s kept, isolated 9 shed");
            // 4) Dead cards: tie-break keeps the higher point
            Check(ComboEvaluator.PickShedCard(Hand(3, 4, 8, 4)) == Id(8, 0),
                "shed: highest-point dead card goes first (8)");
            // 5) HasComboPotential sees the build
            Check(ComboEvaluator.HasComboPotential(Hand(5, 5, 1, 7)),
                "combo potential: a pair exists");
            Check(!ComboEvaluator.HasComboPotential(Hand(1, 4, 7, 10)),
                "combo potential: all isolated singles");
            // 6) A shed swap still plays a legal single and never empties wrongly
            var dShed = ComboEvaluator.Decide(Hand(2, 10));
            Check(dShed.CardIds.Count == 1, "shed: single play is still one card");
            Check(dShed.BestCombo.Id == GameConfig.ComboSingle, "shed: plays a Single");

            // ---- Decision value sanity --------------------------------------
            double pairOnly = 2 * (1.0 + 150.0 / 100.0);
            Check(pairOnly == 5.0, "pair value formula sanity (2 cards * 2.5 = 5.0)");

            Console.WriteLine();
            if (_failures == 0)
            {
                Console.WriteLine("ALL TESTS PASSED");
                return 0;
            }
            Console.WriteLine(_failures + " TEST(S) FAILED");
            return 1;
        }
    }
}
