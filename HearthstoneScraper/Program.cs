// HearthstoneScraper/Program.cs
using System;
using System.Threading.Tasks;
using HearthstoneScraper.Data;
using HearthstoneScraper.Scrapers;
using System.IO; // <-- DODAJ TEN USING
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HearthstoneScraper
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                 .ConfigureServices((context, services) =>
                 {
                     // --- POCZĄTEK ZMIAN ---

                     // 1. Definiujemy bezpieczną ścieżkę do bazy danych
                     // Pobieramy ścieżkę do folderu "Dokumenty" bieżącego użytkownika
                     var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                     var dbPath = Path.Combine(documentsPath, "HearthstoneScraper", "hearthstone_leaderboard.db");

                     // Upewniamy się, że folder istnieje
                     Directory.CreateDirectory(Path.GetDirectoryName(dbPath));

                     // Informujemy użytkownika, gdzie będzie baza
                     Console.WriteLine($"Baza danych zostanie utworzona w: {dbPath}");

                     // 2. Dodaj DbContext, używając pełnej ścieżki
                     services.AddDbContext<AppDbContext>(options =>
                         options.UseSqlite($"Data Source={dbPath}"));

                     // --- KONIEC ZMIAN ---

                     // 3. Dodaj HttpClient do komunikacji z API
                     services.AddHttpClient();

                     // 4. Zarejestruj nasz scraper
                     services.AddTransient<LeaderboardScraper>();
                 })
                 .Build();

            // Automatycznie zastosuj migracje przy starcie aplikacji
            // To stworzy bazę danych, jeśli jej nie ma
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
            }

            // Uruchom główną logikę
            var scraper = host.Services.GetRequiredService<LeaderboardScraper>();
            await scraper.RunAsync();

            Console.WriteLine("\nNaciśnij dowolny klawisz, aby zamknąć...");
            Console.ReadKey();
        }
    }
}