// HearthstoneScraper/Scrapers/LeaderboardScraper.cs
using HearthstoneScraper.ApiModels;
using HearthstoneScraper.Data;
using HearthstoneScraper.Data.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;

namespace HearthstoneScraper.Scrapers
{
    public class LeaderboardScraper
    {
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _dbContext;
        private const string ApiUrlTemplate = "https://hearthstone.blizzard.com/api/community/leaderboardsData?region=EU&leaderboardId=battlegrounds&page={0}";

        // Zamiast tworzyć obiekty, otrzymujemy je przez Dependency Injection
        public LeaderboardScraper(HttpClient httpClient, AppDbContext dbContext)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        }

        public async Task RunAsync()
        {
            Console.WriteLine("Rozpoczynanie procesu scrapowania...");

            // 1. Pobierz dane z API
            var response = await FetchFirstPageAsync();
            if (response == null) return;

            var allApiPlayers = await FetchAllPlayersAsync(response.Leaderboard.Pagination.TotalPages);

            // 2. Przygotuj i zapisz dane do bazy
            await ProcessAndSaveDataAsync(allApiPlayers, response.SeasonId);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Proces scrapowania zakończony pomyślnie!");
            Console.ResetColor();
        }

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

            Console.WriteLine($"Przetwarzanie {livePlayers.Count} graczy...");

            // Krok 1: Pobierz lub stwórz sezon (tak jak wcześniej)
            var season = await _dbContext.Seasons.FirstOrDefaultAsync(s => s.BlizzardId == apiSeasonId);
            if (season == null)
            {
                season = new Season { BlizzardId = apiSeasonId, Name = $"Battlegrounds Season {apiSeasonId}" };
                _dbContext.Seasons.Add(season);
                await _dbContext.SaveChangesAsync(); // Zapisujemy od razu, by mieć Season.Id
            }

            // Krok 2: Pobierz WSZYSTKICH istniejących graczy z bazy ZA JEDNYM RAZEM i umieść w słowniku dla szybkiego dostępu.
            var existingPlayers = await _dbContext.Players
                .Where(p => p.Region == region)
                .ToDictionaryAsync(p => p.BattleTag);

            var newHistoryEntries = new List<RankHistory>();

            // Krok 3: Przejdź przez graczy z API i przetwarzaj ich W PAMIĘCI
            foreach (var apiPlayer in livePlayers)
            {
                // Sprawdź, czy gracz istnieje w naszym słowniku
                if (!existingPlayers.TryGetValue(apiPlayer.AccountId, out var dbPlayer))
                {
                    // Jeśli nie istnieje, stwórz go i dodaj do kontekstu ORAZ do naszego słownika
                    dbPlayer = new Player { BattleTag = apiPlayer.AccountId, Region = region };
                    _dbContext.Players.Add(dbPlayer);
                    existingPlayers.Add(dbPlayer.BattleTag, dbPlayer); // Dodajemy do słownika, by go nie tworzyć ponownie
                }

                // Stwórz wpis w historii. EF Core sam przypisze PlayerId po zapisie.
                newHistoryEntries.Add(new RankHistory
                {
                    Player = dbPlayer, // Przypisujemy cały obiekt gracza
                    Season = season,   // Przypisujemy cały obiekt sezonu
                    ScrapeTimestamp = scrapeTimestamp,
                    Rank = apiPlayer.Rank,
                    Rating = apiPlayer.Rating
                });
            }

            // Krok 4: Dodaj wszystkie nowe wpisy historii do kontekstu ZA JEDNYM RAZEM
            _dbContext.RankHistory.AddRange(newHistoryEntries);

            // Krok 5: Zapisz WSZYSTKIE zmiany (nowych graczy i nową historię) w JEDNEJ transakcji.
            Console.WriteLine("Zapisywanie zmian w bazie danych...");
            int changes = await _dbContext.SaveChangesAsync();
            Console.WriteLine($"Zapisano {changes} zmian w bazie danych.");
        }
        private Task<LeaderboardApiResponse> GetApiResponseAsync(string url) => _httpClient.GetFromJsonAsync<LeaderboardApiResponse>(url);
        private Task<LeaderboardApiResponse> FetchFirstPageAsync() => GetApiResponseAsync(string.Format(ApiUrlTemplate, 1));
    }
}