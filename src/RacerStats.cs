namespace WestlandRacerMod
{
    /// <summary>
    /// Session statistics, shown in the overlay and kept across races. All
    /// members are updated under a lock because the settle notices arrive on the
    /// game's network thread.
    /// </summary>
    internal sealed class RacerStats
    {
        private readonly object _sync = new object();

        public int MatchesStarted;
        public int RacesFinished;          // match fully settled (TotalRank != 0)
        public int RoundsSettled;
        public int RoundWins;              // finished a round in 1st place
        public int FinalWins;              // finished a match in 1st place
        public long FinalRankSum;
        public int CardsPlayed;
        public int PlaysMade;
        public int BiggestPlay;            // most cards in a single UseCardRequest
        public int BestComboMultiple;      // highest combo Multiple ever played
        public string BestComboName = "-";
        public int PropsUsed;

        public void RegisterPlay(ComboEvaluator.PlayDecision decision)
        {
            if (decision.CardIds == null) return;
            lock (_sync)
            {
                PlaysMade++;
                CardsPlayed += decision.CardIds.Count;
                if (decision.CardIds.Count > BiggestPlay) BiggestPlay = decision.CardIds.Count;
                if (decision.BestCombo.Multiple > BestComboMultiple)
                {
                    BestComboMultiple = decision.BestCombo.Multiple;
                    BestComboName = decision.BestCombo.Name;
                }
            }
        }

        public void RegisterSettle(uint roundId, uint rank, long totalScore, uint totalRank)
        {
            lock (_sync)
            {
                RoundsSettled++;
                if (rank == 1) RoundWins++;
                if (totalRank != 0)
                {
                    RacesFinished++;
                    FinalRankSum += totalRank;
                    if (totalRank == 1) FinalWins++;
                }
            }
        }

        public string AverageRank()
        {
            lock (_sync)
            {
                if (RacesFinished <= 0) return "-";
                return (FinalRankSum / (double)RacesFinished).ToString("F2");
            }
        }
    }
}
