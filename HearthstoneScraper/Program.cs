// HearthstoneScraper/Program.cs
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HearthstoneScraper.Data;
using HearthstoneScraper.Scrapers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Serilog;

namespace HearthstoneScraper
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Konfiguracja Seriloga. Jest uniwersalna i pozostaje bez zmian.
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

            var host = CreateHostBuilder(args).Build();

            // --- NOWA, POPRAWNA STRUKTURA URUCHOMIENIA ---
            // Tworzymy "scope", aby poprawnie zarządzać cyklem życia usług, takich jak DbContext.
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;

                try
                {
                    Log.Information("Uruchamianie aplikacji...");

                    // 1. Automatyczne migracje (pobierane z nowo utworzonego zakresu).
                    var dbContext = services.GetRequiredService<AppDbContext>();
                    await dbContext.Database.MigrateAsync();

                    // 2. Uruchomienie głównej logiki (również pobierane z tego samego zakresu).
                    var scraper = services.GetRequiredService<LeaderboardScraper>();
                    await scraper.RunAsync();

                    Log.Information("Aplikacja zakończyła działanie pomyślnie.");
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex.GetBaseException(), "Aplikacja zakończyła działanie z powodu nieoczekiwanego błędu.");
                }
                finally
                {
                    // Upewniamy się, że wszystkie logi zostaną zapisane.
                    await Log.CloseAndFlushAsync();
                }
            }
        }

        // Metoda CreateHostBuilder pozostaje BEZ ZMIAN.
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                })
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    var connectionString = context.Configuration.GetConnectionString("DefaultConnection");
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("Nie znaleziono ConnectionString 'DefaultConnection' w konfiguracji.");
                    }

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(connectionString));

                    var retryPolicy = HttpPolicyExtensions
                        .HandleTransientHttpError()
                        .Or<HttpRequestException>()
                        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                            onRetry: (outcome, timespan, retryAttempt, context) =>
                            {
                                Log.Warning("Błąd zapytania do API. Status: {StatusCode}, Błąd: {ExceptionMessage}. Ponawianie za {TimeSpan}. Próba {RetryAttempt}/3",
                                    outcome.Result?.StatusCode, outcome.Exception?.GetBaseException().Message, timespan, retryAttempt);
                            });

                    services.AddSingleton<IAsyncPolicy<HttpResponseMessage>>(retryPolicy);
                    services.AddHttpClient<LeaderboardScraper>();
                    services.AddTransient<LeaderboardScraper>();
                });
    }
}