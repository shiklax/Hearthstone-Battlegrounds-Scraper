namespace HearthstoneScraper.Core.Dtos
{
    // Prosty obiekt do reprezentowania jednego punktu na wykresie.
    public class PlayerRatingPointDto
    {
        public DateTime Timestamp { get; set; }
        public int Rating { get; set; }
    }
}