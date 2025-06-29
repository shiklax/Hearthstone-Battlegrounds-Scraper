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
        while (true)
        {
            AnsiConsole.Clear();
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
                    "Przeglądaj cały ranking (TOP 1000)",
                    "Wyszukaj historię gracza",
                    "Pokaż wykres ratingu gracza",
                    "Najwięksi wygrani/przegrani (24h)",
                    "Porównaj dwóch graczy (wykres)",
                    "Pokaż szczegółowe statystyki gracza",
                    "Pokaż statystyki bazy",
                    "Zakończ"
                    }));

            switch (choice)
            {
                case "Wyświetl aktualny TOP 25 leaderboard":
                    await ShowTopLeaderboardAsync();
                    break;
                case "Przeglądaj cały ranking (TOP 1000)":
                    await BrowseFullLeaderboardAsync();
                    break;
                case "Wyszukaj historię gracza":
                    await ShowPlayerHistoryAsync();
                    break;
                case "Pokaż wykres ratingu gracza":
                    await ShowPlayerRatingChartAsync();
                    break;
                case "Najwięksi wygrani/przegrani (24h)":
                    await ShowDailyMoversAsync();
                    break;
                case "Pokaż statystyki bazy":
                    await ShowDbStatsAsync();
                    break;
                case "Porównaj dwóch graczy (wykres)":
                    await ComparePlayersChartAsync();
                    break;
                case "Pokaż szczegółowe statystyki gracza": // <<< NOWA OPCJA
                    await ShowPlayerStatsAsync();
                    break;
                case "Zakończ":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Naciśnij dowolny klawisz, aby wrócić do menu...[/]");
            Console.ReadKey(true);
        }
    }

    // W pliku HearthstoneScraper.Viewer/Program.cs, w klasie UserInterface

    private async Task BrowseFullLeaderboardAsync()
    {
        var latestTimestamp = await _db.RankHistory.MaxAsync(rh => (DateTime?)rh.ScrapeTimestamp);
        if (latestTimestamp == null) { AnsiConsole.MarkupLine("[red]Brak danych w bazie![/]"); return; }

        // Pobieramy WSZYSTKICH graczy z ostatniej migawki do pamięci
        var allPlayers = await _db.RankHistory
            .Where(rh => rh.ScrapeTimestamp == latestTimestamp && rh.Rank != null)
            .Include(rh => rh.Player)
            .Select(rh => new { rh.Rank, rh.Player.BattleTag, rh.Rating })
            .ToListAsync();

        // --- Zmienne stanu dla interaktywnego widoku ---
        string sortBy = "Rank";
        bool ascending = true;
        int currentPage = 1;
        const int pageSize = 20; // Ile rekordów na stronę

        while (true)
        {
            // 1. Sortowanie danych
            var sortedPlayers = sortBy switch
            {
                "BattleTag" => ascending ? allPlayers.OrderBy(p => p.BattleTag, StringComparer.OrdinalIgnoreCase).ToList() : allPlayers.OrderByDescending(p => p.BattleTag, StringComparer.OrdinalIgnoreCase).ToList(),
                "Rating" => ascending ? allPlayers.OrderBy(p => p.Rating).ToList() : allPlayers.OrderByDescending(p => p.Rating).ToList(),
                _ => ascending ? allPlayers.OrderBy(p => p.Rank).ToList() : allPlayers.OrderByDescending(p => p.Rank).ToList(),
            };

            // 2. Paginacja
            int totalPlayers = sortedPlayers.Count;
            int totalPages = (int)Math.Ceiling(totalPlayers / (double)pageSize);
            if (currentPage < 1) currentPage = 1;
            if (currentPage > totalPages) currentPage = totalPages;

            var playersOnPage = sortedPlayers
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // 3. Rysowanie tabeli
            AnsiConsole.Clear();
            var table = new Table().Expand().Border(TableBorder.Rounded);

            string title = $"[cyan]Pełny Ranking (Strona {currentPage}/{totalPages})[/]";
            table.Title(title);

            table.AddColumn(sortBy == "Rank" ? $"Rank {(ascending ? '▲' : '▼')}" : "Rank");
            table.AddColumn(sortBy == "BattleTag" ? $"BattleTag {(ascending ? '▲' : '▼')}" : "BattleTag");
            table.AddColumn(sortBy == "Rating" ? $"Rating {(ascending ? '▲' : '▼')}" : "Rating");

            foreach (var entry in playersOnPage)
            {
                table.AddRow(entry.Rank.ToString(), entry.BattleTag, entry.Rating.ToString());
            }
            AnsiConsole.Write(table);

            // 4. Wyświetlanie menu i nawigacji
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[grey]Nawigacja:[/] ");
            AnsiConsole.Markup("[yellow][[←]][/] Poprzednia | Następna [yellow][[→]][/]");
            AnsiConsole.WriteLine(); // Przejdź do nowej linii

            AnsiConsole.Markup("[grey]Sortuj:[/] ");
            AnsiConsole.Markup("[yellow][[R]][/]ank, [yellow][[B]][/]attleTag, [yellow][[R]][/]ating | ");
            AnsiConsole.Markup("[grey]Kierunek:[/] [yellow][[Spacja]][/] | ");
            AnsiConsole.Markup("[grey]Wyjdź:[/] [yellow][[Q]][/]");
            AnsiConsole.WriteLine();
            var key = Console.ReadKey(true).Key;

            // 5. Obsługa akcji użytkownika
            switch (key)
            {
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return; // Wyjdź z pętli

                case ConsoleKey.LeftArrow:
                    if (currentPage > 1) currentPage--;
                    break;

                case ConsoleKey.RightArrow:
                    if (currentPage < totalPages) currentPage++;
                    break;

                case ConsoleKey.Spacebar:
                    ascending = !ascending;
                    break;

                case ConsoleKey.R:
                    sortBy = "Rank";
                    ascending = true; // Domyślnie rank sortujemy rosnąco
                    break;

                case ConsoleKey.B:
                    sortBy = "BattleTag";
                    break;

                case ConsoleKey.T:
                    sortBy = "Rating";
                    ascending = false; // Domyślnie rating sortujemy malejąco
                    break;
            }
        }
    }

    // W pliku HearthstoneScraper.Viewer/Program.cs, w klasie UserInterface

    private async Task ShowPlayerStatsAsync()
    {
        var battleTag = AnsiConsole.Ask<string>("Wpisz [green]BattleTag[/] gracza, którego statystyki chcesz zobaczyć:");

        // Pobieramy całą historię gracza, która ma rating
        var playerHistory = await _db.RankHistory
            .Include(h => h.Player)
            .Where(h => h.Player.BattleTag.ToLower() == battleTag.ToLower() && h.Rating.HasValue)
            .OrderBy(h => h.ScrapeTimestamp)
            .Select(h => new { h.Rating, h.ScrapeTimestamp }) // Bierzemy tylko potrzebne dane
            .ToListAsync();

        if (playerHistory.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]Nie znaleziono żadnych danych rankingowych dla gracza: {battleTag}[/]");
            return;
        }

        // --- Obliczanie statystyk ---
        var peakRating = playerHistory.Max(h => h.Rating.Value);
        var lowestRating = playerHistory.Min(h => h.Rating.Value);
        var currentRating = playerHistory.Last().Rating.Value;
        var averageRating = (int)playerHistory.Average(h => h.Rating.Value);

        // Obliczanie zmian dziennych
        var dailyChanges = playerHistory
            .GroupBy(h => h.ScrapeTimestamp.Date) // Grupujemy po dacie (bez godziny)
            .Select(dayGroup => {
                var first = dayGroup.First().Rating.Value;
                var last = dayGroup.Last().Rating.Value;
                return last - first;
            })
            .ToList();

        var biggestGain = dailyChanges.Any() ? dailyChanges.Max() : 0;
        var biggestLoss = dailyChanges.Any() ? dailyChanges.Min() : 0;

        // Obliczanie dni w rankingu
        var totalDaysTracked = await _db.RankHistory
            .Where(h => h.Player.BattleTag.ToLower() == battleTag.ToLower())
            .Select(h => h.ScrapeTimestamp.Date)
            .Distinct()
            .CountAsync();

        var daysInRanking = playerHistory.Select(h => h.ScrapeTimestamp.Date).Distinct().Count();
        var daysOutsideRanking = totalDaysTracked - daysInRanking;

        // --- Wyświetlanie statystyk ---
        var panel = new Panel(
            new Table()
                .Border(TableBorder.None)
                .AddColumn(new TableColumn("Statystyka").RightAligned())
                .AddColumn(new TableColumn("Wartość").LeftAligned())
                .AddRow("[bold]Najwyższy Rating:[/]", $"[yellow]{peakRating}[/]")
                .AddRow("[bold]Najniższy Rating:[/]", $"[dim]{lowestRating}[/]")
                .AddRow("[bold]Aktualny Rating:[/]", $"[white]{currentRating}[/]")
                .AddRow("[bold]Średni Rating:[/]", $"[aqua]~{averageRating}[/]")
                .AddRow("[bold]Największy dzienny zysk:[/]", $"[green]+{biggestGain}[/]")
                .AddRow("[bold]Największa dzienna strata:[/]", $"[red]{biggestLoss}[/]")
                .AddRow("[bold]Dni w rankingu:[/]", $"[green]{daysInRanking}[/]")
                .AddRow("[bold]Dni poza rankingiem:[/]", $"[red]{daysOutsideRanking}[/]")
        )
        .Header($"[bold white]Statystyki dla: {battleTag}[/]")
        .Border(BoxBorder.Rounded)
        .Expand();

        AnsiConsole.Write(panel);
    }

    private async Task ComparePlayersChartAsync()
    {
        var battleTag1 = AnsiConsole.Ask<string>("Wpisz [green]BattleTag[/] pierwszego gracza:");
        var battleTag2 = AnsiConsole.Ask<string>("Wpisz [cyan]BattleTag[/] drugiego gracza:");

        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var history = await _db.RankHistory
            .Include(h => h.Player)
            .Where(h => (h.Player.BattleTag.ToLower() == battleTag1.ToLower() || h.Player.BattleTag.ToLower() == battleTag2.ToLower())
                        && h.Rating.HasValue && h.ScrapeTimestamp >= thirtyDaysAgo)
            .OrderBy(h => h.ScrapeTimestamp)
            .ToListAsync();

        var player1History = history.Where(h => h.Player.BattleTag.ToLower() == battleTag1.ToLower()).ToList();
        var player2History = history.Where(h => h.Player.BattleTag.ToLower() == battleTag2.ToLower()).ToList();

        if (player1History.Count < 2 || player2History.Count < 2)
        {
            AnsiConsole.MarkupLine("[red]Nie znaleziono wystarczających danych dla obu graczy, aby narysować wykres porównawczy.[/]");
            return;
        }

        // --- Normalizacja danych (bez zmian) ---
        var allTimestamps = player1History.Select(h => h.ScrapeTimestamp)
            .Union(player2History.Select(h => h.ScrapeTimestamp))
            .OrderBy(t => t)
            .ToList();
        var player1Ratings = player1History.ToDictionary(h => h.ScrapeTimestamp, h => h.Rating.Value);
        var player2Ratings = player2History.ToDictionary(h => h.ScrapeTimestamp, h => h.Rating.Value);
        var ys1 = new List<double>();
        var ys2 = new List<double>();
        double lastRating1 = player1Ratings.Values.First();
        double lastRating2 = player2Ratings.Values.First();
        foreach (var timestamp in allTimestamps)
        {
            if (player1Ratings.TryGetValue(timestamp, out var rating1)) lastRating1 = rating1;
            if (player2Ratings.TryGetValue(timestamp, out var rating2)) lastRating2 = rating2;
            ys1.Add(lastRating1);
            ys2.Add(lastRating2);
        }
        double[] xs = Enumerable.Range(1, allTimestamps.Count).Select(i => (double)i).ToArray();

        // --- SEKCJA TWORZENIA WYKRESU (WERSJA FINALNA) ---
        const int plotWidth = 90;
        const int plotHeight = 22;

        var plt = new Plot(width: plotWidth, height: plotHeight);

        // Konfiguracja wyglądu
        plt.Axis.Pen = new LinePen(SystemLineBrushes.Double, ConsoleColor.White);
        plt.Grid.Pen = new LinePen(SystemLineBrushes.Dotted, ConsoleColor.DarkGray);

        // <<< ZMIANA: PRZYWRACAMY ETYKIETY I ZNACZNIKI DLA OSI Y >>>
        plt.Ticks.IsVisible = true;
        //plt.Ticks.ShowTicksOn.X = false; // NADAL wyłączamy dla X
        //plt.Ticks.ShowTicksOn.Y = true;  // WŁĄCZAMY dla Y

        plt.Ticks.Labels.IsVisible = true;
        //plt.Ticks.Labels.ShowLabelsOn.X = false; // NADAL wyłączamy dla X
        //plt.Ticks.Labels.ShowLabelsOn.Y = true;  // WŁĄCZAMY dla Y
        plt.Ticks.Labels.Color = ConsoleColor.Cyan;
        plt.Ticks.Labels.Format = "F0";

        // Dodajemy serie danych
        plt.AddSeries(xs, ys1.ToArray(), new PointPen(SystemPointBrushes.Braille, ConsoleColor.Green));
        plt.AddSeries(xs, ys2.ToArray(), new PointPen(SystemPointBrushes.Braille, ConsoleColor.Cyan));

        // Rysowanie i renderowanie
        AnsiConsole.MarkupLine($"\n[bold yellow]Porównanie graczy:[/]");
        plt.Draw();
        plt.Render();

        // Ręczne rysowanie etykiet dla osi X z datą początkową, środkową i końcową
        string startDate = allTimestamps.First().ToString("MMM dd");
        string midDate = allTimestamps[allTimestamps.Count / 2].ToString("MMM dd");
        string endDate = allTimestamps.Last().ToString("MMM dd");

        var axisLine = new string(' ', plotWidth + 8); // +8 to margines na etykiety osi Y
        var axisBuilder = new System.Text.StringBuilder(axisLine);

        // Wstawiamy daty, uwzględniając margines po lewej stronie
        int leftMargin = 8;
        axisBuilder.Insert(leftMargin, startDate);
        axisBuilder.Insert(leftMargin + plotWidth / 2 - midDate.Length / 2, midDate);
        axisBuilder.Remove(leftMargin + plotWidth - endDate.Length, endDate.Length).Insert(leftMargin + plotWidth - endDate.Length, endDate);

        AnsiConsole.WriteLine(axisBuilder.ToString().Substring(0, leftMargin + plotWidth));

        // Legenda
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]■[/] - {battleTag1}");
        AnsiConsole.MarkupLine($"[cyan]■[/] - {battleTag2}");
    }

    private async Task ShowDailyMoversAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Obliczanie zmian w rankingu z ostatnich 24 godzin...[/]");

        var twentyFourHoursAgo = DateTime.UtcNow.AddHours(-24);

        // Krok 1: Wykonujemy zapytanie do bazy, które jest "proste" dla EF Core
        // Pobieramy tylko ID gracza i jego wpisy, a nie całe obiekty.
        var playerEntries = await _db.RankHistory
            .Where(h => h.ScrapeTimestamp >= twentyFourHoursAgo && h.Rating.HasValue)
            .GroupBy(h => h.PlayerId) // Grupujemy po ID
            .Where(g => g.Count() >= 2) // Bierzemy tylko grupy z co najmniej 2 wpisami
            .Select(g => new {
                PlayerId = g.Key,
                Entries = g.OrderBy(h => h.ScrapeTimestamp) // Bierzemy wszystkie wpisy posortowane
                           .Select(h => new { h.Rating, h.ScrapeTimestamp }) // I tylko potrzebne dane
                           .ToList()
            })
            .ToListAsync(); // Materializujemy wyniki w pamięci

        if (!playerEntries.Any())
        {
            AnsiConsole.MarkupLine("[red]Brak wystarczających danych do pokazania zmian w rankingu.[/]");
            AnsiConsole.MarkupLine("[grey]Upewnij się, że scraper został uruchomiony co najmniej dwa razy w ciągu ostatnich 24 godzin dla tych samych graczy.[/]");
            return;
        }

        // Krok 2: Przetwarzamy wyniki w pamięci, co jest już bezpieczne
        var movers = playerEntries
            .Select(p => new {
                p.PlayerId,
                Change = p.Entries.Last().Rating.Value - p.Entries.First().Rating.Value, // Obliczamy zmianę
                CurrentRating = p.Entries.Last().Rating.Value
            })
            .OrderByDescending(p => p.Change) // Sortujemy
            .Take(20) // Bierzemy TOP 20
            .ToList();

        // Krok 3: Pobieramy BattleTagi dla naszej topowej 20-tki jednym zapytaniem
        var playerIds = movers.Select(m => m.PlayerId).ToList();
        var playersDict = await _db.Players
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.BattleTag);

        // Tworzymy tabelę do wyświetlenia wyników
        var table = new Table().Expand().Border(TableBorder.Rounded);
        table.Title("[cyan]Największe zmiany w rankingu (ostatnie 24h)[/]");
        table.AddColumn("Miejsce").AddColumn("BattleTag").AddColumn("Zmiana Ratingu").AddColumn("Aktualny Rating");

        int rank = 1;
        foreach (var mover in movers)
        {
            string changeString = mover.Change > 0 ? $"[green]+{mover.Change}[/]"
                                : mover.Change < 0 ? $"[red]{mover.Change}[/]"
                                : "[grey]0[/]";

            table.AddRow(
                rank.ToString(),
                playersDict[mover.PlayerId], // Pobieramy BattleTag ze słownika
                changeString,
                mover.CurrentRating.ToString()
            );
            rank++;
        }

        AnsiConsole.Write(table);
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