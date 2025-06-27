// HearthstoneScraper/Scrapers/LeaderboardScraper.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json; // <-- Ważny using
using System.Text.Json;
using System.Threading.Tasks;
using HearthstoneScraper.ApiModels;
using HearthstoneScraper.Data;
using HearthstoneScraper.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly; // <-- Ważny using
using Polly.Retry; // <-- Ważny using

namespace HearthstoneScraper.Scrapers
{
    public class LeaderboardScraper
    {
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<LeaderboardScraper> _logger;
        private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy; // <<< NOWE POLE
        private const string ApiUrlTemplate = "https://hearthstone.blizzard.com/api/community/leaderboardsData?region=EU&leaderboardId=battlegrounds&page={0}"; // <<< CELOWO BŁĘDNY URL DO TESTÓW

        public LeaderboardScraper(
            HttpClient httpClient,
            AppDbContext dbContext,
            ILogger<LeaderboardScraper> logger,
            IAsyncPolicy<HttpResponseMessage> retryPolicy) // <<< NOWY PARAMETR
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _logger = logger;
            _retryPolicy = retryPolicy; // <<< PRZYPISANIE

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        }

        public async Task RunAsync()
        {
            _logger.LogInformation("Rozpoczynanie procesu scrapowania...");

            try
            {
                var response = await FetchFirstPageAsync();
                if (response == null)
                {
                    _logger.LogError("Nie udało się pobrać pierwszej strony danych z API. Przerywanie pracy.");
                    return;
                }

                var allApiPlayers = await FetchAllPlayersAsync(response.Leaderboard.Pagination.TotalPages);
                await ProcessAndSaveDataAsync(allApiPlayers, response.SeasonId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas pobierania danych z API po wszystkich próbach ponowienia.");
                // Rzucamy wyjątek dalej, aby główny try-catch w Program.cs go złapał jako Fatal
                throw;
            }
        }

        // <<< CAŁKOWICIE NOWA, ODPORNA NA BŁĘDY WERSJA METODY GET >>>
        private async Task<LeaderboardApiResponse> GetApiResponseAsync(string url)
        {
            // "Opakowujemy" nasze zapytanie w politykę Polly.
            HttpResponseMessage response = await _retryPolicy.ExecuteAsync(() =>
                _httpClient.GetAsync(url));

            // Jeśli po wszystkich próbach odpowiedź wciąż nie jest sukcesem, rzuć wyjątek.
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<LeaderboardApiResponse>();
        }

        // Reszta metod (FetchAllPlayersAsync, ProcessAndSaveDataAsync) bez zmian,
        // ale teraz będą one automatycznie korzystać z odpornej metody GetApiResponseAsync.
        private Task<LeaderboardApiResponse> FetchFirstPageAsync() => GetApiResponseAsync(string.Format(ApiUrlTemplate, 1));

        private async Task<List<LeaderboardRow>> FetchAllPlayersAsync(int totalPages)
        {
            var allPlayers = new List<LeaderboardRow>();
            var tasks = new List<Task<LeaderboardApiResponse>>();
            for (int i = 1; i <= totalPages; i++)
            {
                string url = string.Format(ApiUrlTemplate, i);
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

        private async Task ProcessAndSaveDataAsync(List<LeaderboardRow> livePlayers, int apiSeasonId)
        {
            var region = "EU";
            var scrapeTimestamp = DateTime.UtcNow;

            _logger.LogInformation("Przetwarzanie {PlayerCount} graczy z API...", livePlayers.Count);

            var season = await _dbContext.Seasons.FirstOrDefaultAsync(s => s.BlizzardId == apiSeasonId);
            if (season == null)
            {
                _logger.LogInformation("Nie znaleziono sezonu ID: {SeasonId}. Tworzenie nowego.", apiSeasonId);
                season = new Season { BlizzardId = apiSeasonId, Name = $"Battlegrounds Season {apiSeasonId}" };
                _dbContext.Seasons.Add(season);
                await _dbContext.SaveChangesAsync();
            }

            var livePlayerBattleTags = livePlayers.Select(p => p.AccountId).ToHashSet();
            var allTrackedPlayers = await _dbContext.Players
                .Where(p => p.Region == region && p.History.Any(h => h.SeasonId == season.Id))
                .ToListAsync();
            var missingPlayers = allTrackedPlayers
                .Where(p => !livePlayerBattleTags.Contains(p.BattleTag))
                .ToList();

            _logger.LogInformation("Znaleziono {MissingCount} graczy, którzy spadli z rankingu.", missingPlayers.Count);

            var newHistoryEntries = new List<RankHistory>();
            var existingPlayersDict = allTrackedPlayers.ToDictionary(p => p.BattleTag);

            foreach (var apiPlayer in livePlayers)
            {
                if (!existingPlayersDict.TryGetValue(apiPlayer.AccountId, out var dbPlayer))
                {
                    dbPlayer = new Player { BattleTag = apiPlayer.AccountId, Region = region };
                    _dbContext.Players.Add(dbPlayer);
                }
                newHistoryEntries.Add(new RankHistory
                {
                    Player = dbPlayer,
                    Season = season,
                    ScrapeTimestamp = scrapeTimestamp,
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
                    ScrapeTimestamp = scrapeTimestamp,
                    Rank = null,
                    Rating = null
                });
            }

            if (newHistoryEntries.Any())
            {
                _dbContext.RankHistory.AddRange(newHistoryEntries);

                _logger.LogInformation("Zapisywanie zmian w bazie danych...");
                int changes = await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Zapisano {ChangeCount} zmian w bazie danych.", changes);
            }
            else
            {
                _logger.LogInformation("Brak nowych zmian do zapisania.");
            }
        }
    }
}