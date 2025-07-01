using System;

namespace HearthstoneScraper.Data.Models
{
    public class RankHistory
    {
        public int Id { get; set; }
        public DateTime ScrapeTimestamp { get; set; }
        public int? Rank { get; set; }
        public int? Rating { get; set; }
        public int PlayerId { get; set; }
        public virtual Player Player { get; set; }
        public int SeasonId { get; set; }
        public virtual Season Season { get; set; }

        // <<< DODAJ TE DWIE LINIE >>>
        public int LeaderboardId { get; set; }
        public virtual Leaderboard Leaderboard { get; set; } = null!;
    }
}