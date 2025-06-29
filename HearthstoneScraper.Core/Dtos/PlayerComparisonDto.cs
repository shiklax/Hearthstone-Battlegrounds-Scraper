namespace HearthstoneScraper.Core.Dtos
{
    public class PlayerComparisonDto
    {
        public string BattleTag1 { get; set; } = string.Empty;
        public List<PlayerRatingPointDto> History1 { get; set; } = new();

        public string BattleTag2 { get; set; } = string.Empty;
        public List<PlayerRatingPointDto> History2 { get; set; } = new();
    }
}