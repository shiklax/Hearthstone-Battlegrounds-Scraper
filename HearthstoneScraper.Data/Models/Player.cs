using System.Collections.Generic;

namespace HearthstoneScraper.Data.Models
{
    public class Player
    {
        public int Id { get; set; }
        public string BattleTag { get; set; }
        public string Region { get; set; }
        public virtual ICollection<RankHistory> History { get; set; }
    }
}