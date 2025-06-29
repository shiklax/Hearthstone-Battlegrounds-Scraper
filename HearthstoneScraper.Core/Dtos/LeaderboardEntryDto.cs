namespace HearthstoneScraper.Core.Dtos
{
    // Prosty obiekt do reprezentowania jednego wiersza w leaderboardzie.
    public class LeaderboardEntryDto
    {
        public int? Rank { get; set; }
        public string BattleTag { get; set; } = string.Empty;
        public int? Rating { get; set; }
        public DateTime ScrapeTimestamp { get; set; }
    }
}