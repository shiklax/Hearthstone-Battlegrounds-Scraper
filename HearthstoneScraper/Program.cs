// HearthstoneScraper/Program.cs
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HearthstoneScraper.Data;
using HearthstoneScraper.Scrapers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly; // Ważny using
using Polly.Extensions.Http; // Ważny using
using Serilog;

namespace HearthstoneScraper
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Konfiguracja Seriloga (bez zmian)
            string logFileName = $"logs/scraper_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Default", Serilog.Events.LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(logFileName)
                .CreateLogger();

            try
            {
                Log.Information("Uruchamianie aplikacji...");

                var host = Host.CreateDefaultBuilder(args)
                    .UseSerilog()
                    .ConfigureServices((context, services) =>
                    {
                        // Definiujemy ścieżkę do bazy (bez zmian)
                        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        var dbPath = Path.Combine(documentsPath, "HearthstoneScraper", "hearthstone_leaderboard.db");
                        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

                        services.AddDbContext<AppDbContext>(options =>
                            options.UseSqlite($"Data Source={dbPath}"));

                        // --- NOWA KONFIGURACJA Z RĘCZNYM WYKORZYSTANIEM POLLY ---

                        // 1. Tworzymy politykę ponawiania prób
                        var retryPolicy = HttpPolicyExtensions
                            .HandleTransientHttpError()
                            .Or<HttpRequestException>() // Reaguj na błędy HTTP ORAZ na wszystkie błędy sieciowe
                            .WaitAndRetryAsync(
                                3,
                                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                                onRetry: (outcome, timespan, retryAttempt, context) =>
                                {
                                    var statusCode = outcome.Result?.StatusCode;
                                    var exceptionMessage = outcome.Exception?.GetBaseException().Message;
                                    Log.Warning("Zapytanie do API nie powiodło się. Status: {StatusCode}, Błąd: {ExceptionMessage}. Czekam {TimeSpan} przed ponowieniem. Próba {RetryAttempt}/3",
                                        statusCode, exceptionMessage, timespan, retryAttempt);
                                }
                            );

                        // 2. Rejestrujemy politykę w kontenerze DI jako Singleton
                        services.AddSingleton<IAsyncPolicy<HttpResponseMessage>>(retryPolicy);

                        // 3. Rejestrujemy HttpClient w standardowy sposób
                        services.AddHttpClient<LeaderboardScraper>();

                        // --- KONIEC ZMIAN ---

                        // Rejestrujemy nasz scraper
                        services.AddTransient<LeaderboardScraper>();
                    })
                    .Build();

                using (var scope = host.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Database.Migrate();
                }

                var scraper = host.Services.GetRequiredService<LeaderboardScraper>();
                await scraper.RunAsync();

                Log.Information("Aplikacja zakończyła działanie pomyślnie.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex.GetBaseException(), "Aplikacja zakończyła działanie z powodu nieoczekiwanego błędu.");
            }
            finally
            {
                await Log.CloseAndFlushAsync();
                Console.WriteLine("\nNaciśnij dowolny klawisz, aby zamknąć...");
                Console.ReadKey();
            }
        }
    }
}