using HearthstoneScraper.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace HearthstoneScraper.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Player> Players { get; set; }
        public DbSet<Season> Seasons { get; set; }
        public DbSet<RankHistory> RankHistory { get; set; }

        // Konstruktor potrzebny do Dependency Injection
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Gwarantuje, że nie będzie dwóch graczy o tym samym tagu i regionie
            modelBuilder.Entity<Player>()
                .HasIndex(p => new { p.BattleTag, p.Region })
                .IsUnique();
        }
    }
}