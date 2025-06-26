using ConsolePlot;
using ConsolePlot.Drawing.Tools;
using HearthstoneScraper.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.IO;
using System.Linq;
using System.Text; // <-- Może być potrzebny ten using
using System.Threading.Tasks;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Reszta kodu bez zmian
        var host = CreateHostBuilder(args).Build();
        var ui = host.Services.GetRequiredService<UserInterface>();
        await ui.RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            // --- POCZĄTEK ZMIAN ---
            .ConfigureLogging(logging =>
            {
                // Usuwamy wszystkich domyślnych dostawców logowania (np. konsolę)
                logging.ClearProviders();

                // Możesz tu dodać własnego dostawcę, jeśli chcesz logować błędy do pliku
                // np. logging.AddSerilog(...)
                // Na razie zostawiamy puste, aby nic się nie wyświetlało.
            })
            // --- KONIEC ZMIAN ---
            .ConfigureServices((context, services) =>
            {
                // Definiujemy ścieżkę do bazy danych
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var dbPath = Path.Combine(documentsPath, "HearthstoneScraper", "hearthstone_leaderboard.db");

                // Rejestrujemy DbContext
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));

                // Rejestrujemy naszą klasę UI
                services.AddTransient<UserInterface>();
            });
}

// Klasa odpowiedzialna za cały interfejs użytkownika
public class UserInterface
{
    private readonly AppDbContext _db;

    public UserInterface(AppDbContext dbContext)
    {
        _db = dbContext;
    }

    public async Task RunAsync()
    {
        while (true) // Pętla główna programu
        {
            // 1. Zawsze na początku czyść ekran, aby przygotować go na menu.
            AnsiConsole.Clear();

            // 2. Wyświetl logo i menu na czystym ekranie.
            AnsiConsole.Write(
                new FigletText("HS BG Scraper")
                    .Centered()
                    .Color(Color.Yellow));
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Co chcesz zrobić?")
                    .PageSize(10)
                    .AddChoices(new[]
                    {
                    "Wyświetl aktualny TOP 25 leaderboard",
                    "Wyszukaj historię gracza",
                    "Pokaż wykres ratingu gracza (30 dni)",
                    "Pokaż statystyki bazy",
                    "Zakończ"
                    }));

            // 3. Wykonaj akcję. Wyniki pojawią się pod menu.
            switch (choice)
            {
                case "Wyświetl aktualny TOP 25 leaderboard":
                    await ShowTopLeaderboardAsync();
                    break;
                case "Wyszukaj historię gracza":
                    await ShowPlayerHistoryAsync();
                    break;
                case "Pokaż wykres ratingu gracza (30 dni)": // <<< NOWA OPCJA W MENU
                    await ShowPlayerRatingChartAsync();
                    break;
                case "Pokaż statystyki bazy":
                    await ShowDbStatsAsync();
                    break;
                case "Zakończ":
                    return; // Wyjście z pętli i programu
            }

            // 4. Po wyświetleniu wyników, poczekaj na reakcję użytkownika.
            // Pętla `while` zacznie się od nowa, wyczyści ekran (krok 1) i znów pokaże menu.
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Naciśnij dowolny klawisz, aby wrócić do menu...[/]");
            Console.ReadKey(true); // `true` ukrywa wciśnięty znak w konsoli
        }
    }

    // W pliku HearthstoneScraper.Viewer/Program.cs, w klasie UserInterface

    private async Task ShowPlayerRatingChartAsync()
    {
        var battleTag = AnsiConsole.Ask<string>("Wpisz [green]BattleTag[/] gracza, którego wykres chcesz zobaczyć:");

        // Pobieramy historię gracza z ostatnich 30 dni, gdzie rating nie jest pusty
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var playerHistory = await _db.RankHistory
            .Include(rh => rh.Player)
            .Where(rh => rh.Player.BattleTag.ToLower() == battleTag.ToLower()
                         && rh.Rating.HasValue
                         && rh.ScrapeTimestamp >= thirtyDaysAgo)
            .OrderBy(rh => rh.ScrapeTimestamp)
            .ToListAsync();

        if (playerHistory.Count < 2)
        {
            AnsiConsole.MarkupLine($"[red]Nie znaleziono wystarczających danych dla gracza '{battleTag}' z ostatnich 30 dni.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"\n[bold yellow]Wykres ratingu dla gracza: {playerHistory.First().Player.BattleTag}[/]");
        AnsiConsole.MarkupLine($"[grey]Okres: {playerHistory.First().ScrapeTimestamp:yyyy-MM-dd} - {playerHistory.Last().ScrapeTimestamp:yyyy-MM-dd}[/]");
        AnsiConsole.MarkupLine("[grey]Oś Y: Rating | Oś X: Kolejne odczyty w czasie[/]"); // <-- NOWA LINIA
        // --- Przygotowanie danych (Oś X jako numery odczytów, Oś Y jako rating) ---
        double[] xs = Enumerable.Range(1, playerHistory.Count).Select(i => (double)i).ToArray();
        double[] ys = playerHistory.Select(h => (double)h.Rating.Value).ToArray();

        // --- Tworzenie i konfiguracja wykresu (zgodnie z przykładem 'AllSettingsDemonstration') ---

        // 1. Stwórz obiekt Plot
        var plt = new Plot(width: 80, height: 20);

        // 2. Skonfiguruj wygląd wykresu, aby był czytelny
        plt.Axis.IsVisible = true;
        plt.Axis.Pen = new LinePen(SystemLineBrushes.Double, ConsoleColor.White);

        plt.Grid.IsVisible = true;
        plt.Grid.Pen = new LinePen(SystemLineBrushes.Dotted, ConsoleColor.DarkGray);

        plt.Ticks.IsVisible = true;
        plt.Ticks.Pen = new LinePen(SystemLineBrushes.Thin, ConsoleColor.Gray);

        plt.Ticks.Labels.IsVisible = true;
        plt.Ticks.Labels.Color = ConsoleColor.Cyan;
        plt.Ticks.Labels.Format = "F0"; // Formatowanie bez miejsc po przecinku (dla ratingu)

        // 3. Dodaj serię danych
        plt.AddSeries(xs, ys, new PointPen(SystemPointBrushes.Braille, ConsoleColor.Yellow));

        // 4. Narysuj wszystko w buforze
        plt.Draw();

        // 5. Wyświetl gotowy wykres w konsoli
        plt.Render();
    }

    private async Task ShowTopLeaderboardAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Pobieranie aktualnego rankingu...[/]");

        // Używamy LINQ do odtworzenia logiki naszego widoku SQL
        var latestTimestamp = await _db.RankHistory.MaxAsync(rh => (DateTime?)rh.ScrapeTimestamp);

        if (latestTimestamp == null)
        {
            AnsiConsole.MarkupLine("[red]Brak danych w bazie![/]");
            return;
        }

        var topPlayers = await _db.RankHistory
            .Where(rh => rh.ScrapeTimestamp == latestTimestamp && rh.Rank != null)
            .Include(rh => rh.Player) // Dołączamy dane gracza (jak JOIN w SQL)
            .OrderBy(rh => rh.Rank)
            .Take(25)
            .ToListAsync();

        var table = new Table().Expand().Border(TableBorder.Rounded);
        table.Title("[cyan]TOP 25 Graczy Battlegrounds (EU)[/]");
        table.AddColumn("Rank");
        table.AddColumn("BattleTag");
        table.AddColumn("Rating");

        foreach (var entry in topPlayers)
        {
            table.AddRow(
                entry.Rank.ToString(),
                entry.Player.BattleTag,
                entry.Rating.ToString()
            );
        }

        AnsiConsole.Write(table);
    }

    private async Task ShowPlayerHistoryAsync()
    {
        var battleTag = AnsiConsole.Ask<string>("Wpisz [green]BattleTag[/] gracza, którego chcesz wyszukać:");

        var playerHistory = await _db.RankHistory
            .Include(rh => rh.Player)
            .Where(rh => rh.Player.BattleTag.ToLower() == battleTag.ToLower())
            .OrderByDescending(rh => rh.ScrapeTimestamp)
            .Take(20) // Pokaż ostatnie 20 wpisów
            .ToListAsync();

        if (!playerHistory.Any())
        {
            AnsiConsole.MarkupLine($"[red]Nie znaleziono gracza o BattleTagu: {battleTag}[/]");
            return;
        }

        var table = new Table().Expand().Border(TableBorder.Rounded);
        table.Title($"[cyan]Historia dla gracza: {playerHistory.First().Player.BattleTag}[/]");
        table.AddColumn("Data zapisu");
        table.AddColumn("Rank");
        table.AddColumn("Rating");

        foreach (var entry in playerHistory)
        {
            table.AddRow(
                entry.ScrapeTimestamp.ToString("yyyy-MM-dd HH:mm"),
                entry.Rank?.ToString() ?? "[grey]N/A[/]", // ?? obsługuje wartości NULL
                entry.Rating?.ToString() ?? "[grey]N/A[/]"
            );
        }

        AnsiConsole.Write(table);
    }

    private async Task ShowDbStatsAsync()
    {
        int playerCount = await _db.Players.CountAsync();
        int historyCount = await _db.RankHistory.CountAsync();

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Statystyka");
        table.AddColumn("Wartość");
        table.AddRow("Liczba śledzonych graczy", playerCount.ToString());
        table.AddRow("Liczba wpisów w historii", historyCount.ToString());

        AnsiConsole.Write(table);
    }
}