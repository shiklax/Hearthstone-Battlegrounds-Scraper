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
        public async Task<PlayerStatsDto?> GetPlayerStatsAsync(string battleTag)
        {
            var player = await _db.Players.FirstOrDefaultAsync(p => p.BattleTag.ToLower() == battleTag.ToLower());
            if (player == null)
            {
                return null; // Gracz nie znaleziony
            }

            var playerHistory = await _db.RankHistory
                .Where(h => h.PlayerId == player.Id && h.Rating.HasValue)
                .OrderBy(h => h.ScrapeTimestamp)
                .Select(h => new { h.Rating, h.ScrapeTimestamp })
                .ToListAsync();

            if (playerHistory.Count == 0)
            {
                return new PlayerStatsDto { BattleTag = player.BattleTag }; // Gracz istnieje, ale nie ma historii ratingu
            }

            var dailyChanges = playerHistory
                .GroupBy(h => h.ScrapeTimestamp.Date)
                .Select(dayGroup => {
                    var first = dayGroup.First().Rating.Value;
                    var last = dayGroup.Last().Rating.Value;
                    return last - first;
                })
                .ToList();

            var totalDaysTracked = await _db.RankHistory
                .Where(h => h.PlayerId == player.Id)
                .Select(h => h.ScrapeTimestamp.Date)
                .Distinct()
                .CountAsync();

            var daysInRanking = playerHistory.Select(h => h.ScrapeTimestamp.Date).Distinct().Count();

            // Tworzymy i zwracamy obiekt DTO z obliczonymi statystykami
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

        public async Task<List<DailyMoverDto>> GetDailyMoversAsync()
        {
            var twentyFourHoursAgo = DateTime.UtcNow.AddHours(-24);

            var playerEntries = await _db.RankHistory
                .Where(h => h.ScrapeTimestamp >= twentyFourHoursAgo && h.Rating.HasValue)
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
                return new List<DailyMoverDto>(); // Zwróć pustą listę, jeśli nie ma danych
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

            // Bierzemy 10 największych zysków
            var topGainers = movers.Take(10);
            // Bierzemy 10 największych strat (odwracamy sortowanie i bierzemy 10)
            var topLosers = movers.OrderBy(m => m.Change).Take(10);

            // Łączymy obie listy i usuwamy duplikaty (jeśli gracz był w obu)
            return topGainers.Union(topLosers).OrderByDescending(m => m.Change).ToList();
        }

        public async Task<List<LeaderboardEntryDto>> GetFullLeaderboardAsync()
        {
            var latestTimestamp = await _db.RankHistory.MaxAsync(rh => (DateTime?)rh.ScrapeTimestamp);
            if (latestTimestamp == null) return new List<LeaderboardEntryDto>();

            return await _db.RankHistory
                .Where(rh => rh.ScrapeTimestamp == latestTimestamp && rh.Rank != null)
                .Include(rh => rh.Player)
                .Select(rh => new LeaderboardEntryDto
                {
                    Rank = rh.Rank,
                    BattleTag = rh.Player.BattleTag,
                    Rating = rh.Rating
                })
                .ToListAsync();
        }
        public async Task<List<LeaderboardEntryDto>> GetPlayerHistoryAsync(string battleTag)
        {
            return await _db.RankHistory
                .Include(rh => rh.Player)
                .Where(rh => rh.Player.BattleTag.ToLower() == battleTag.ToLower())
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
        public async Task<(string BattleTag, List<PlayerRatingPointDto> History)?> GetPlayerChartDataAsync(string battleTag)
        {
            var player = await _db.Players.FirstOrDefaultAsync(p => p.BattleTag.ToLower() == battleTag.ToLower());
            if (player == null) return null;

            var history = await _db.RankHistory
                .Where(h => h.PlayerId == player.Id && h.Rating.HasValue)
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
        public async Task<PlayerComparisonDto?> GetPlayerComparisonDataAsync(string battleTag1, string battleTag2)
        {
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            // Pobieramy historię obu graczy jednym zapytaniem
            var history = await _db.RankHistory
                .Include(h => h.Player)
                .Where(h => (h.Player.BattleTag.ToLower() == battleTag1.ToLower() || h.Player.BattleTag.ToLower() == battleTag2.ToLower())
                            && h.Rating.HasValue && h.ScrapeTimestamp >= thirtyDaysAgo)
                .OrderBy(h => h.ScrapeTimestamp)
                .Select(h => new {
                    h.Player.BattleTag,
                    RatingPoint = new PlayerRatingPointDto { Timestamp = h.ScrapeTimestamp, Rating = h.Rating!.Value }
                })
                .ToListAsync();

            var history1 = history.Where(h => h.BattleTag.ToLower() == battleTag1.ToLower()).Select(h => h.RatingPoint).ToList();
            var history2 = history.Where(h => h.BattleTag.ToLower() == battleTag2.ToLower()).Select(h => h.RatingPoint).ToList();

            if (history1.Count < 2 || history2.Count < 2)
            {
                return null; // Niewystarczająco danych
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