namespace HearthstoneScraper.Core.Dtos
{
    public class DailyMoverDto
    {
        public string BattleTag { get; set; } = string.Empty;
        public int Change { get; set; }
        public int CurrentRating { get; set; }
    }
}