using HearthstoneScraper.Core.Dtos;
using HearthstoneScraper.Data;
using Microsoft.EntityFrameworkCore;

namespace HearthstoneScraper.Core.Services
{
    public class LeaderboardService
    {
        private readonly AppDbContext _db;

        public LeaderboardService(AppDbContext dbContext)
        {
            _db = dbContext;
        }

        // Ta metoda będzie zawierała logikę przeniesioną z UserInterface
        public async Task<PlayerStatsDto?> GetPlayerStatsAsync(string battleTag, int leaderboardId, string region)
        {
            // Szukamy gracza w konkretnym regionie
            var player = await _db.Players.FirstOrDefaultAsync(p => p.BattleTag.ToLower() == battleTag.ToLower() && p.Region == region);
            if (player == null)
            {
                return null; // Gracz nie znaleziony w tym regionie
            }

            // Pobieramy historię gracza tylko z wybranego leaderboardu
            var playerHistory = await _db.RankHistory
                .Where(h => h.PlayerId == player.Id && h.LeaderboardId == leaderboardId && h.Rating.HasValue)
                .OrderBy(h => h.ScrapeTimestamp)
                .Select(h => new { h.Rating, h.ScrapeTimestamp })
                .ToListAsync();

            if (playerHistory.Count == 0)
            {
                return new PlayerStatsDto { BattleTag = player.BattleTag };
            }

            var dailyChanges = playerHistory
                .GroupBy(h => h.ScrapeTimestamp.Date)
                .Select(dayGroup => dayGroup.Last().Rating.Value - dayGroup.First().Rating.Value)
                .ToList();

            // Liczymy dni, uwzględniając konkretny leaderboard
            var totalDaysTracked = await _db.RankHistory
                .Where(h => h.PlayerId == player.Id && h.LeaderboardId == leaderboardId)
                .Select(h => h.ScrapeTimestamp.Date)
                .Distinct()
                .CountAsync();

            var daysInRanking = playerHistory.Select(h => h.ScrapeTimestamp.Date).Distinct().Count();

            return new PlayerStatsDto
            {
                BattleTag = player.BattleTag,
                PeakRating = playerHistory.Max(h => h.Rating!.Value),
                LowestRating = playerHistory.Min(h => h.Rating!.Value),
                CurrentRating = playerHistory.Last().Rating!.Value,
                AverageRating = (int)playerHistory.Average(h => h.Rating!.Value),
                BiggestDailyGain = dailyChanges.Any() ? dailyChanges.Max() : 0,
                BiggestDailyLoss = dailyChanges.Any() ? dailyChanges.Min() : 0,
                DaysInRanking = daysInRanking,
                DaysOutsideRanking = totalDaysTracked - daysInRanking
            };
        }

// W LeaderboardService.cs

public async Task<List<DailyMoverDto>> GetDailyMoversAsync(int leaderboardId, string region)
{
    var twentyFourHoursAgo = DateTime.UtcNow.AddHours(-24);

    var playerEntries = await _db.RankHistory
        // Dodajemy filtrowanie po regionie i leaderboardzie
        .Where(h => h.Player.Region == region && h.LeaderboardId == leaderboardId &&
                     h.ScrapeTimestamp >= twentyFourHoursAgo && h.Rating.HasValue)
        .GroupBy(h => h.PlayerId)
        .Where(g => g.Count() >= 2)
        .Select(g => new {
            PlayerId = g.Key,
            OldestRating = g.OrderBy(h => h.ScrapeTimestamp).First().Rating!.Value,
            NewestRating = g.OrderBy(h => h.ScrapeTimestamp).Last().Rating!.Value
        })
        .ToListAsync();

    if (!playerEntries.Any())
    {
        return new List<DailyMoverDto>();
    }

    var playerIds = playerEntries.Select(p => p.PlayerId).ToList();
    var playersDict = await _db.Players
        .Where(p => playerIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id, p => p.BattleTag);

    var movers = playerEntries
        .Select(p => new DailyMoverDto
        {
            BattleTag = playersDict[p.PlayerId],
            Change = p.NewestRating - p.OldestRating,
            CurrentRating = p.NewestRating
        })
        .OrderByDescending(p => p.Change)
        .ToList();

    var topGainers = movers.Take(10);
    var topLosers = movers.OrderBy(m => m.Change).Take(10);
    
    return topGainers.Union(topLosers).OrderByDescending(m => m.Change).ToList();
}

        // W pliku HearthstoneScraper.Core/Services/LeaderboardService.cs

        public async Task<List<LeaderboardEntryDto>> GetFullLeaderboardAsync(int leaderboardId, string region)
        {
            // Krok 1: Znajdź najnowszy, rzeczywisty wpis w historii dla danego leaderboardu
            var latestEntry = await _db.RankHistory
                .Where(rh => rh.LeaderboardId == leaderboardId && rh.Player.Region == region)
                .OrderByDescending(rh => rh.ScrapeTimestamp)
                .FirstOrDefaultAsync(); // Bierzemy cały, pierwszy obiekt

            // Jeśli w ogóle nie ma żadnych wpisów, zwróć pustą listę
            if (latestEntry == null)
            {
                return new List<LeaderboardEntryDto>();
            }

            // Krok 2: Użyj timestampa z tego konkretnego wpisu do filtrowania
            var exactTimestamp = latestEntry.ScrapeTimestamp;

            // Krok 3: Pobierz wszystkie rekordy, które mają DOKŁADNIE ten sam, prawdziwy timestamp
            return await _db.RankHistory
                .Where(rh => rh.LeaderboardId == leaderboardId
                             && rh.ScrapeTimestamp == exactTimestamp
                             && rh.Player.Region == region
                             && rh.Rank != null)
                .Include(rh => rh.Player)
                .Select(rh => new LeaderboardEntryDto
                {
                    Rank = rh.Rank,
                    BattleTag = rh.Player.BattleTag,
                    Rating = rh.Rating
                })
                .OrderBy(dto => dto.Rank)
                .ToListAsync();
        }
        public async Task<List<LeaderboardEntryDto>> GetPlayerHistoryAsync(string battleTag, int leaderboardId, string region)
        {
            return await _db.RankHistory
                .Include(rh => rh.Player)
                // Filtrujemy po wszystkich trzech kryteriach
                .Where(rh => rh.Player.BattleTag.ToLower() == battleTag.ToLower()
                             && rh.Player.Region == region
                             && rh.LeaderboardId == leaderboardId)
                .OrderByDescending(rh => rh.ScrapeTimestamp)
                .Select(rh => new LeaderboardEntryDto
                {
                    Rank = rh.Rank,
                    BattleTag = rh.Player.BattleTag,
                    Rating = rh.Rating,
                    ScrapeTimestamp = rh.ScrapeTimestamp
                })
                .Take(20)
                .ToListAsync();
        }
        public async Task<(string BattleTag, List<PlayerRatingPointDto> History)?> GetPlayerChartDataAsync(string battleTag, int leaderboardId, string region)
        {
            // Szukamy gracza w konkretnym regionie
            var player = await _db.Players.FirstOrDefaultAsync(p => p.BattleTag.ToLower() == battleTag.ToLower() && p.Region == region);
            if (player == null) return null;

            // Pobieramy historię tylko z wybranego leaderboardu
            var history = await _db.RankHistory
                .Where(h => h.PlayerId == player.Id && h.LeaderboardId == leaderboardId && h.Rating.HasValue)
                .OrderBy(h => h.ScrapeTimestamp)
                .Take(30)
                .Select(h => new PlayerRatingPointDto { Timestamp = h.ScrapeTimestamp, Rating = h.Rating!.Value })
                .ToListAsync();

            if (history.Count < 2)
            {
                return null;
            }

            return (player.BattleTag, history);
        }
        public async Task<DbStatsDto> GetDbStatsAsync()
        {
            return new DbStatsDto
            {
                PlayerCount = await _db.Players.CountAsync(),
                HistoryCount = await _db.RankHistory.CountAsync()
            };
        }
        public async Task<PlayerComparisonDto?> GetPlayerComparisonDataAsync(string battleTag1, string battleTag2, int leaderboardId, string region)
        {
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            // Szukamy graczy tylko w wybranym regionie
            string lowerBattleTag1 = battleTag1.ToLower();
            string lowerBattleTag2 = battleTag2.ToLower();

            var history = await _db.RankHistory
                .Include(h => h.Player)
                .Where(h => h.Player.Region == region // <<< FILTROWANIE PO REGIONIE
                            && (h.Player.BattleTag.ToLower() == lowerBattleTag1 || h.Player.BattleTag.ToLower() == lowerBattleTag2)
                            && h.LeaderboardId == leaderboardId // <<< FILTROWANIE PO LEADERBOARDZIE
                            && h.Rating.HasValue
                            && h.ScrapeTimestamp >= thirtyDaysAgo)
                .OrderBy(h => h.ScrapeTimestamp)
                .Select(h => new {
                    h.Player.BattleTag,
                    RatingPoint = new PlayerRatingPointDto { Timestamp = h.ScrapeTimestamp, Rating = h.Rating!.Value }
                })
                .ToListAsync();

            // Dalsza logika bez zmian
            var history1 = history.Where(h => h.BattleTag.ToLower() == lowerBattleTag1).Select(h => h.RatingPoint).ToList();
            var history2 = history.Where(h => h.BattleTag.ToLower() == lowerBattleTag2).Select(h => h.RatingPoint).ToList();

            if (history1.Count < 2 || history2.Count < 2)
            {
                return null;
            }

            return new PlayerComparisonDto
            {
                BattleTag1 = battleTag1,
                History1 = history1,
                BattleTag2 = battleTag2,
                History2 = history2
            };
        }
    }
}