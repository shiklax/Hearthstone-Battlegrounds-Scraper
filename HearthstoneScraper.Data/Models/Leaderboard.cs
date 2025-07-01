namespace HearthstoneScraper.Data.Models
{
    public class Leaderboard
    {
        public int Id { get; set; }
        public string ApiId { get; set; } = string.Empty; // np. "battlegrounds", "battlegroundsduo"
        public string Name { get; set; } = string.Empty; // np. "Battlegrounds Solo", "Battlegrounds Duo"
    }
}