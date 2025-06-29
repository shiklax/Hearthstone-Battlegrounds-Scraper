namespace HearthstoneScraper.Core.Dtos
{
    public class PlayerStatsDto
    {
        public string BattleTag { get; set; } = string.Empty;
        public int PeakRating { get; set; }
        public int LowestRating { get; set; }
        public int CurrentRating { get; set; }
        public int AverageRating { get; set; }
        public int BiggestDailyGain { get; set; }
        public int BiggestDailyLoss { get; set; }
        public int DaysInRanking { get; set; }
        public int DaysOutsideRanking { get; set; }
    }
}