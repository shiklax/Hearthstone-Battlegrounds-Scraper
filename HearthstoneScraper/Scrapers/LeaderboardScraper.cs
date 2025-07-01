using HearthstoneScraper.ApiModels;
using HearthstoneScraper.Configuration; // Ważny using
using HearthstoneScraper.Data;
using HearthstoneScraper.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options; // Ważny using
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace HearthstoneScraper.Scrapers
{
    public class LeaderboardScraper
    {
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<LeaderboardScraper> _logger;
        private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;
        private readonly List<ScrapeTarget> _targets;

        public LeaderboardScraper(
            HttpClient httpClient,
            AppDbContext dbContext,
            ILogger<LeaderboardScraper> logger,
            IAsyncPolicy<HttpResponseMessage> retryPolicy,
            IOptions<List<ScrapeTarget>> scrapeTargets)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _logger = logger;
            _retryPolicy = retryPolicy;
            _targets = scrapeTargets.Value;

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        }

        // Metoda główna, która iteruje po wszystkich celach
        public async Task RunAsync()
        {
            _logger.LogInformation("Rozpoczynanie procesu scrapowania dla {TargetCount} celów...", _targets.Count);

            foreach (var target in _targets)
            {
                _logger.LogInformation("--- Rozpoczynanie pracy dla: {Region} - {LeaderboardName} ---", target.Region, target.Name);
                try
                {
                    // Budujemy szablon URL na podstawie celu z pętli
                    string apiUrlTemplate = $"https://hearthstone.blizzard.com/en-us/api/community/leaderboardsData?region={target.Region}&leaderboardId={target.LeaderboardId}&page={{0}}";

                    var firstPageResponse = await GetApiResponseAsync(string.Format(apiUrlTemplate, 1));
                    if (firstPageResponse?.Leaderboard == null)
                    {
                        _logger.LogError("Nie udało się pobrać danych dla {Region}-{Name}. Pomijanie.", target.Region, target.Name);
                        continue;
                    }

                    var allApiPlayers = await FetchAllPlayersAsync(apiUrlTemplate, firstPageResponse.Leaderboard.Pagination.TotalPages);
                    await ProcessAndSaveDataAsync(allApiPlayers, firstPageResponse.SeasonId, target);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Wystąpił nieoczekiwany błąd podczas przetwarzania celu {Region}-{Name}", target.Region, target.Name);
                }
            }
        }

        // Metoda pomocnicza do pobierania danych, teraz przyjmuje szablon URL
        private async Task<List<LeaderboardRow>> FetchAllPlayersAsync(string apiUrlTemplate, int totalPages)
        {
            const int pagesToFetch = 40; // TOP 1000
            _logger.LogInformation("Pobieranie danych z {PagesCount} stron API...", pagesToFetch);

            var allPlayers = new List<LeaderboardRow>();
            var tasks = new List<Task<LeaderboardApiResponse?>>();

            for (int i = 1; i <= pagesToFetch; i++)
            {
                string url = string.Format(apiUrlTemplate, i);
                tasks.Add(GetApiResponseAsync(url));
            }

            var responses = await Task.WhenAll(tasks);
            foreach (var response in responses)
            {
                if (response?.Leaderboard?.Rows != null)
                {
                    allPlayers.AddRange(response.Leaderboard.Rows);
                }
            }
            return allPlayers.OrderBy(p => p.Rank).ToList();
        }

        // Metoda pomocnicza do zapisu, teraz przyjmuje obiekt celu
        private async Task ProcessAndSaveDataAsync(List<LeaderboardRow> livePlayers, int apiSeasonId, ScrapeTarget target)
        {
            var distinctLivePlayers = livePlayers.GroupBy(p => p.AccountId).Select(g => g.First()).ToList();
            if (livePlayers.Count != distinctLivePlayers.Count)
            {
                _logger.LogWarning("Wykryto i usunięto {DuplicateCount} zduplikowanych graczy z danych API dla {Target}.", livePlayers.Count - distinctLivePlayers.Count, target.Name);
            }
            _logger.LogInformation("Przetwarzanie {PlayerCount} unikalnych graczy z API dla {Target}.", distinctLivePlayers.Count, target.Name);

            // Pobierz lub stwórz Season i Leaderboard
            var season = await _dbContext.Seasons.FirstOrDefaultAsync(s => s.BlizzardId == apiSeasonId);
            if (season == null)
            {
                season = new Season { BlizzardId = apiSeasonId, Name = $"Battlegrounds Season {apiSeasonId}" };
                _dbContext.Seasons.Add(season);
            }
            var leaderboard = await _dbContext.Leaderboards.FirstOrDefaultAsync(l => l.ApiId == target.LeaderboardId);
            if (leaderboard == null)
            {
                leaderboard = new Leaderboard { ApiId = target.LeaderboardId, Name = target.Name };
                _dbContext.Leaderboards.Add(leaderboard);
            }
            await _dbContext.SaveChangesAsync();

            var livePlayerBattleTags = distinctLivePlayers.Select(p => p.AccountId).ToHashSet();
            var allTrackedPlayers = await _dbContext.Players
                .Where(p => p.Region == target.Region && p.History.Any(h => h.SeasonId == season.Id && h.LeaderboardId == leaderboard.Id))
                .ToListAsync();

            var missingPlayers = allTrackedPlayers.Where(p => !livePlayerBattleTags.Contains(p.BattleTag)).ToList();
            _logger.LogInformation("Znaleziono {MissingCount} graczy, którzy spadli z rankingu {Target}.", missingPlayers.Count, target.Name);

            var newHistoryEntries = new List<RankHistory>();
            var existingPlayersDict = allTrackedPlayers.ToDictionary(p => p.BattleTag);

            foreach (var apiPlayer in distinctLivePlayers)
            {
                if (!existingPlayersDict.TryGetValue(apiPlayer.AccountId, out var dbPlayer))
                {
                    dbPlayer = await _dbContext.Players.FirstOrDefaultAsync(p => p.BattleTag == apiPlayer.AccountId && p.Region == target.Region);
                    if (dbPlayer == null)
                    {
                        dbPlayer = new Player { BattleTag = apiPlayer.AccountId, Region = target.Region };
                        _dbContext.Players.Add(dbPlayer);
                    }
                }
                newHistoryEntries.Add(new RankHistory
                {
                    Player = dbPlayer,
                    SeasonId = season.Id,
                    LeaderboardId = leaderboard.Id,
                    ScrapeTimestamp = DateTime.UtcNow,
                    Rank = apiPlayer.Rank,
                    Rating = apiPlayer.Rating
                });
            }

            foreach (var missingPlayer in missingPlayers)
            {
                newHistoryEntries.Add(new RankHistory
                {
                    PlayerId = missingPlayer.Id,
                    SeasonId = season.Id,
                    LeaderboardId = leaderboard.Id,
                    ScrapeTimestamp = DateTime.UtcNow,
                    Rank = null,
                    Rating = null
                });
            }

            if (newHistoryEntries.Any())
            {
                _dbContext.RankHistory.AddRange(newHistoryEntries);
                _logger.LogInformation("Zapisywanie {ChangeCount} zmian w bazie danych dla {Target}...", newHistoryEntries.Count, target.Name);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Zmiany zapisane pomyślnie.");
            }
            else
            {
                _logger.LogInformation("Brak nowych zmian do zapisania dla {Target}.", target.Name);
            }
        }

        // Metoda do komunikacji z API, używa Polly
        private async Task<LeaderboardApiResponse?> GetApiResponseAsync(string url)
        {
            HttpResponseMessage response = await _retryPolicy.ExecuteAsync(() => _httpClient.GetAsync(url));
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<LeaderboardApiResponse>();
        }
    }
}