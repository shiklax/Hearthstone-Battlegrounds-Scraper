using System.Collections.Generic;
namespace HearthstoneScraper.Data.Models
{
    public class Season
    {
        public int Id { get; set; }
        public int BlizzardId { get; set; } // ID z API Blizzarda
        public string Name { get; set; }
        public virtual ICollection<RankHistory> History { get; set; }
    }
}