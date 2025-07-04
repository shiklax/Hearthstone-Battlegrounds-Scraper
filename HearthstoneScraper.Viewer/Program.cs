using ConsolePlot;
using ConsolePlot.Drawing.Tools;
using HearthstoneScraper.Core.Dtos;
using HearthstoneScraper.Core.Services;
using HearthstoneScraper.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.IO;
using System.Linq;
using System.Text; // <-- Może być potrzebny ten using
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Extensions.Configuration;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Reszta kodu bez zmian
        var host = CreateHostBuilder(args).Build();
        using (var scope = host.Services.CreateScope())
        {
            var services = scope.ServiceProvider;

            try
            {
                // Pobieramy UI z zakresowego dostawcy usług, a nie z globalnego
                var ui = services.GetRequiredService<UserInterface>();
                await ui.RunAsync();
            }
            catch (Exception ex)
            {
                // Proste logowanie błędu do konsoli, jeśli coś pójdzie nie tak
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
                Console.WriteLine("\nNaciśnij dowolny klawisz, aby zamknąć...");
                Console.ReadKey();
            }
        }
    }


    // W pliku HearthstoneScraper.Viewer/Program.cs

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                // To jest w porządku, zostawiamy - wycisza domyślne logi .NET
                logging.ClearProviders();
            })
            .ConfigureServices((context, services) =>
            {
                var connectionString = context.Configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException("Nie znaleziono ConnectionString 'DefaultConnection'.");
                }
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(connectionString));

                services.AddTransient<LeaderboardService>();
                services.AddTransient<UserInterface>();
            });
}
    // Klasa odpowiedzialna za cały interfejs użytkownika
    public class UserInterface
{
    private readonly IConfiguration _configuration;
    private int _currentLeaderboardId;
    private string _currentRegion = string.Empty;
    private string _currentLeaderboardName = "Nie wybrano";

    private readonly AppDbContext _db;
    private readonly LeaderboardService _leaderboardService;
    public UserInterface(AppDbContext dbContext, LeaderboardService leaderboardService, IConfiguration configuration)
    {
        _db = dbContext;
        _leaderboardService = leaderboardService;
        _configuration = configuration;
    }

    public async Task RunAsync()
    {
        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        var dbHost = connectionString?.Split(';').FirstOrDefault(x => x.StartsWith("Host="))?.Replace("Host=", "");
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(
                new FigletText("HS BG Scraper")
                    .Centered()
                    .Color(Color.Yellow));
            AnsiConsole.WriteLine();


            var infoGrid = new Grid()
    .AddColumn(new GridColumn().PadRight(4)) // Dodajemy kolumnę z odstępem
    .AddColumn();

            infoGrid.AddRow($"[grey]Wersja aplikacji:[/] [bold]{appVersion}[/]", $"[grey]Połączono z bazą:[/] [bold yellow]{dbHost ?? "Brak"}[/]");
            infoGrid.AddRow($"[grey]Aktywny leaderboard:[/] [bold yellow]{_currentRegion} - {_currentLeaderboardName}[/]", "");

            // 2. Tworzymy Panel, który otacza naszą siatkę
            var infoPanel = new Panel(infoGrid)
                .Header("[dim]Informacje o sesji[/]")
                .Border(BoxBorder.Rounded) // Ustawiamy ramkę dla panelu
                .Expand(); // Rozszerzamy panel na całą szerokość konsoli

            // 3. Wyświetlamy gotowy panel
            AnsiConsole.Write(infoPanel);


            AnsiConsole.MarkupLine($"[grey]Aktywny leaderboard: [bold yellow]{_currentRegion} - {_currentLeaderboardName}[/][/]");
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
                    "Zmień aktywny leaderboard",
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
                case "Zmień aktywny leaderboard": // <<< NOWA OPCJA
                    await SetActiveLeaderboardAsync();
                    break; // Używamy 'break', aby pętla się odświeżyła i pokazała nowy stan
                case "Zakończ":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Naciśnij dowolny klawisz, aby wrócić do menu...[/]");
            Console.ReadKey(true);
        }
    }

    private async Task SetActiveLeaderboardAsync()
    {
        var availableLeaderboards = await _db.RankHistory
            .Include(rh => rh.Leaderboard)
            .Include(rh => rh.Player)
            .Select(rh => new {
                LeaderboardId = rh.Leaderboard.Id,
                LeaderboardName = rh.Leaderboard.Name,
                Region = rh.Player.Region
            })
            .Distinct()
            .ToListAsync();

        if (!availableLeaderboards.Any())
        {
            AnsiConsole.MarkupLine("[red]Brak danych w bazie. Uruchom scrapera.[/]");
            _currentLeaderboardName = "Brak danych";
            return;
        }

        var choices = availableLeaderboards
            .Select(l => $"[yellow]{l.Region}[/] - {l.LeaderboardName}")
            .ToList();

        var selectedChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Wybierz aktywny leaderboard[/]")
                .PageSize(10)
                .AddChoices(choices));

        var selectedIndex = choices.IndexOf(selectedChoice);
        var selection = availableLeaderboards[selectedIndex];

        // Ustawiamy stan naszej klasy
        _currentLeaderboardId = selection.LeaderboardId;
        _currentRegion = selection.Region;
        _currentLeaderboardName = selection.LeaderboardName;

        AnsiConsole.MarkupLine($"[green]Aktywny leaderboard ustawiony na: {_currentRegion} - {_currentLeaderboardName}[/]");
        AnsiConsole.MarkupLine("[grey]Naciśnij dowolny klawisz, aby kontynuować...[/]");
        Console.ReadKey(true);
    }
    private async Task BrowseFullLeaderboardAsync()
    {
        // Krok 1: Sprawdź, czy użytkownik wybrał jakikolwiek leaderboard
        if (_currentLeaderboardId == 0)
        {
            AnsiConsole.MarkupLine("[red]Najpierw wybierz aktywny leaderboard z menu głównego![/]");
            return; // Wracamy do menu, nie ma sensu iść dalej
        }

        // Krok 2: Wywołaj serwis z zapisanymi w stanie parametrami
        var allPlayers = await _leaderboardService.GetFullLeaderboardAsync(_currentLeaderboardId, _currentRegion);

        if (!allPlayers.Any())
        {
            AnsiConsole.MarkupLine($"[red]Brak danych w bazie dla [yellow]{_currentRegion} - {_currentLeaderboardName}[/][/]");
            return;
        }

        // Od tego momentu reszta kodu (pętla, sortowanie, paginacja) pozostaje
        // prawie identyczna, ponieważ operuje już na liście w pamięci.
        // Jedyna zmiana to dodanie nazwy leaderboardu do tytułu tabeli.

        string sortBy = "Rank";
        bool ascending = true;
        int currentPage = 1;
        const int pageSize = 20;

        while (true)
        {
            var sortedPlayers = sortBy switch
            {
                "BattleTag" => ascending ? allPlayers.OrderBy(p => p.BattleTag, StringComparer.OrdinalIgnoreCase).ToList() : allPlayers.OrderByDescending(p => p.BattleTag, StringComparer.OrdinalIgnoreCase).ToList(),
                "Rating" => ascending ? allPlayers.OrderBy(p => p.Rating).ToList() : allPlayers.OrderByDescending(p => p.Rating).ToList(),
                _ => ascending ? allPlayers.OrderBy(p => p.Rank).ToList() : allPlayers.OrderByDescending(p => p.Rank).ToList(),
            };

            int totalPlayers = sortedPlayers.Count;
            int totalPages = (int)Math.Ceiling(totalPlayers / (double)pageSize);
            if (currentPage < 1) currentPage = 1;
            if (currentPage > totalPages) currentPage = totalPages;

            var playersOnPage = sortedPlayers.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();

            AnsiConsole.Clear();
            var table = new Table().Expand().Border(TableBorder.Rounded);

            // <<< ZMIANA: Dodajemy nazwę aktywnego leaderboardu do tytułu >>>
            string title = $"[cyan]Pełny Ranking: [yellow]{_currentRegion} - {_currentLeaderboardName}[/] (Strona {currentPage}/{totalPages})[/]";
            table.Title(title);

            table.AddColumn(sortBy == "Rank" ? $"Rank {(ascending ? '▲' : '▼')}" : "Rank");
            table.AddColumn(sortBy == "BattleTag" ? $"BattleTag {(ascending ? '▲' : '▼')}" : "BattleTag");
            table.AddColumn(sortBy == "Rating" ? $"Rating {(ascending ? '▲' : '▼')}" : "Rating");

            foreach (var entry in playersOnPage)
            {
                table.AddRow(entry.Rank.ToString(), entry.BattleTag, entry.Rating.ToString());
            }
            AnsiConsole.Write(table);

            AnsiConsole.Markup("[grey]Nawigacja:[/] [yellow][[←]][/] Poprzednia | Następna [yellow][[→]][/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[grey]Opcje:[/] ");
            AnsiConsole.Markup("[yellow][[R]][/]ank, ");
            AnsiConsole.Markup("[yellow][[B]][/]attleTag, ");
            AnsiConsole.Markup("[yellow][[T]][/]Rating | ");
            AnsiConsole.Markup("[grey]Kierunek:[/] [yellow][[Spacja]][/] | ");
            AnsiConsole.Markup("[grey]Wyjdź:[/] [yellow][[Q]][/]");
            AnsiConsole.WriteLine();

            var key = Console.ReadKey(true).Key;

            switch (key)
            {
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return;
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
                    ascending = true;
                    break;
                case ConsoleKey.B:
                    sortBy = "BattleTag";
                    break;
                case ConsoleKey.T:
                    sortBy = "Rating";
                    ascending = false;
                    break;
            }
        }
    }
    private async Task ShowPlayerStatsAsync()
    {
        // Sprawdzamy, czy użytkownik wybrał leaderboard
        if (_currentLeaderboardId == 0)
        {
            AnsiConsole.MarkupLine("[red]Najpierw wybierz aktywny leaderboard z menu głównego![/]");
            return;
        }

        var battleTag = AnsiConsole.Ask<string>("Wpisz [green]BattleTag[/] gracza, którego statystyki chcesz zobaczyć:");

        AnsiConsole.MarkupLine("[yellow]Obliczanie statystyk...[/]");
        // Przekazujemy do serwisu BattleTag ORAZ dane z aktywnego leaderboardu
        PlayerStatsDto? stats = await _leaderboardService.GetPlayerStatsAsync(battleTag, _currentLeaderboardId, _currentRegion);

        if (stats == null)
        {
            AnsiConsole.MarkupLine($"[red]Gracz o BattleTagu '{battleTag}' nie został znaleziony w regionie [yellow]{_currentRegion}[/][/]");
            return;
        }

        if (stats.DaysInRanking == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Gracz '{stats.BattleTag}' został znaleziony, ale nie ma jeszcze historii ratingu dla leaderboardu [yellow]{_currentLeaderboardName}[/][/]");
            return;
        }

        // Sekcja wyświetlania pozostaje bez zmian, ale dodajemy kontekst do nagłówka
        var panel = new Panel(
            new Table()
                .Border(TableBorder.None)
                .AddColumn(new TableColumn("Statystyka").RightAligned())
                .AddColumn(new TableColumn("Wartość").LeftAligned())
                .AddRow("[bold]Najwyższy Rating:[/]", $"[yellow]{stats.PeakRating}[/]")
                .AddRow("[bold]Najniższy Rating:[/]", $"[dim]{stats.LowestRating}[/]")
                .AddRow("[bold]Aktualny Rating:[/]", $"[white]{stats.CurrentRating}[/]")
                .AddRow("[bold]Średni Rating:[/]", $"[aqua]~{stats.AverageRating}[/]")
                .AddRow("[bold]Największy dzienny zysk:[/]", $"[green]+{stats.BiggestDailyGain}[/]")
                .AddRow("[bold]Największa dzienna strata:[/]", $"[red]{stats.BiggestDailyLoss}[/]")
                .AddRow("[bold]Dni w rankingu:[/]", $"[green]{stats.DaysInRanking}[/]")
                .AddRow("[bold]Dni poza rankingiem:[/]", $"[red]{stats.DaysOutsideRanking}[/]")
        )
        .Header($"[bold white]Statystyki dla: {stats.BattleTag} ({_currentRegion} - {_currentLeaderboardName})[/]")
        .Border(BoxBorder.Rounded)
        .Expand();

        AnsiConsole.Write(panel);
    }
    private async Task ComparePlayersChartAsync()
    {
        if (_currentLeaderboardId == 0)
        {
            AnsiConsole.MarkupLine("[red]Najpierw wybierz aktywny leaderboard z menu głównego![/]");
            return;
        }
        var battleTag1 = AnsiConsole.Ask<string>("Wpisz [green]BattleTag[/] pierwszego gracza:");
        var battleTag2 = AnsiConsole.Ask<string>("Wpisz [cyan]BattleTag[/] drugiego gracza:");

        // 1. Wywołujemy serwis, aby pobrał dane dla obu graczy
        var comparisonData = await _leaderboardService.GetPlayerComparisonDataAsync(battleTag1, battleTag2, _currentLeaderboardId, _currentRegion);

        // Sprawdzamy, czy serwis zwrócił wystarczającą ilość danych
        if (comparisonData == null)
        {
            AnsiConsole.MarkupLine("[red]Nie znaleziono wystarczających danych dla obu graczy, aby narysować wykres porównawczy.[/]");
            return;
        }

        // --- Logika normalizacji danych (specyficzna dla widoku konsolowego) ---
        // Tworzymy wspólną oś czasu, biorąc wszystkie unikalne znaczniki czasu od obu graczy.
        var allTimestamps = comparisonData.History1.Select(h => h.Timestamp)
            .Union(comparisonData.History2.Select(h => h.Timestamp))
            .OrderBy(t => t)
            .ToList();

        // Tworzymy słowniki dla łatwego dostępu do ratingu w danym punkcie czasu
        var player1Ratings = comparisonData.History1.ToDictionary(h => h.Timestamp, h => h.Rating);
        var player2Ratings = comparisonData.History2.ToDictionary(h => h.Timestamp, h => h.Rating);

        // Przygotowujemy serie danych Y. Jeśli gracz nie ma odczytu w danym punkcie,
        // używamy poprzedniej znanej wartości, aby linia na wykresie była ciągła.
        var ys1 = new List<double>();
        var ys2 = new List<double>();
        double lastRating1 = player1Ratings.Values.FirstOrDefault();
        double lastRating2 = player2Ratings.Values.FirstOrDefault();

        foreach (var timestamp in allTimestamps)
        {
            if (player1Ratings.TryGetValue(timestamp, out var rating1)) lastRating1 = rating1;
            if (player2Ratings.TryGetValue(timestamp, out var rating2)) lastRating2 = rating2;
            ys1.Add(lastRating1);
            ys2.Add(lastRating2);
        }

        // Oś X to po prostu kolejne punkty na naszej wspólnej osi czasu
        double[] xs = Enumerable.Range(1, allTimestamps.Count).Select(i => (double)i).ToArray();

        // --- Sekcja tworzenia i renderowania wykresu ---
        const int plotWidth = 90;
        const int plotHeight = 22;

        var plt = new Plot(width: plotWidth, height: plotHeight);

        // Konfiguracja wyglądu
        plt.Axis.Pen = new LinePen(SystemLineBrushes.Double, ConsoleColor.White);
        plt.Grid.Pen = new LinePen(SystemLineBrushes.Dotted, ConsoleColor.DarkGray);

        // Włączamy etykiety i znaczniki tylko dla osi Y (rating)
        plt.Ticks.IsVisible = true;
        plt.Ticks.Labels.IsVisible = true;
        plt.Ticks.Labels.Color = ConsoleColor.Cyan;
        plt.Ticks.Labels.Format = "F0"; // Formatowanie bez miejsc po przecinku

        // Dodajemy obie serie danych z różnymi kolorami
        plt.AddSeries(xs, ys1.ToArray(), new PointPen(SystemPointBrushes.Braille, ConsoleColor.Green));
        plt.AddSeries(xs, ys2.ToArray(), new PointPen(SystemPointBrushes.Braille, ConsoleColor.Cyan));

        // Rysowanie i renderowanie
        AnsiConsole.MarkupLine($"\n[bold yellow]Porównanie graczy w: {_currentRegion} - {_currentLeaderboardName}[/]");
        AnsiConsole.MarkupLine($"\n[bold yellow]Porównanie graczy:[/]");
        plt.Draw();
        plt.Render();

        // Ręczne rysowanie naszej czytelnej osi czasu
        string startDate = allTimestamps.First().ToString("MMM dd");
        string midDate = allTimestamps[allTimestamps.Count / 2].ToString("MMM dd");
        string endDate = allTimestamps.Last().ToString("MMM dd");

        var axisLine = new string(' ', plotWidth + 8);
        var axisBuilder = new System.Text.StringBuilder(axisLine);

        int leftMargin = 8;
        axisBuilder.Insert(leftMargin, startDate);
        axisBuilder.Insert(leftMargin + plotWidth / 2 - midDate.Length / 2, midDate);
        axisBuilder.Remove(leftMargin + plotWidth - endDate.Length, endDate.Length).Insert(leftMargin + plotWidth - endDate.Length, endDate);

        AnsiConsole.WriteLine(axisBuilder.ToString().Substring(0, leftMargin + plotWidth));

        // Legenda
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]■[/] - {comparisonData.BattleTag1}");
        AnsiConsole.MarkupLine($"[cyan]■[/] - {comparisonData.BattleTag2}");
    }
    private async Task ShowDailyMoversAsync()
    {
        if (_currentLeaderboardId == 0)
        {
            AnsiConsole.MarkupLine("[red]Najpierw wybierz aktywny leaderboard z menu głównego![/]");
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Obliczanie zmian w rankingu z ostatnich 24 godzin...[/]");

        // 1. Wywołujemy serwis, aby dostać gotowe dane
        var movers = await _leaderboardService.GetDailyMoversAsync(_currentLeaderboardId, _currentRegion);

        if (!movers.Any())
        {
            AnsiConsole.MarkupLine("[red]Brak wystarczających danych do pokazania zmian w rankingu.[/]");
            AnsiConsole.MarkupLine("[grey]Upewnij się, że scraper został uruchomiony co najmniej dwa razy w ciągu ostatnich 24 godzin dla tych samych graczy.[/]");
            return;
        }

        // 2. Wyświetlamy wyniki w tabeli
        var table = new Table().Expand().Border(TableBorder.Rounded);
        table.Title($"[cyan]Największe zmiany w rankingu: [yellow]{_currentRegion} - {_currentLeaderboardName}[/] (24h)[/]");
        table.AddColumn("Miejsce");
        table.AddColumn("BattleTag");
        table.AddColumn("Zmiana Ratingu");
        table.AddColumn("Aktualny Rating");

        int rank = 1;
        foreach (var mover in movers)
        {
            string changeString = mover.Change > 0 ? $"[green]+{mover.Change}[/]"
                                : mover.Change < 0 ? $"[red]{mover.Change}[/]"
                                : "[grey]0[/]";

            table.AddRow(
                rank.ToString(),
                mover.BattleTag,
                changeString,
                mover.CurrentRating.ToString()
            );
            rank++;
        }

        AnsiConsole.Write(table);
    }
    private async Task ShowPlayerRatingChartAsync()
    {
        // Sprawdzamy, czy użytkownik wybrał leaderboard
        if (_currentLeaderboardId == 0)
        {
            AnsiConsole.MarkupLine("[red]Najpierw wybierz aktywny leaderboard z menu głównego![/]");
            return;
        }

        var battleTag = AnsiConsole.Ask<string>("Wpisz [green]BattleTag[/] gracza, którego wykres chcesz zobaczyć:");

        // Wywołujemy serwis z parametrami z aktywnego stanu
        var result = await _leaderboardService.GetPlayerChartDataAsync(battleTag, _currentLeaderboardId, _currentRegion);

        if (result == null) // Uproszczone sprawdzanie
        {
            AnsiConsole.MarkupLine($"[red]Nie znaleziono wystarczających danych dla gracza '{battleTag}' w [yellow]{_currentRegion} - {_currentLeaderboardName}[/][/]");
            return;
        }

        var (playerName, history) = result.Value;

        double[] xs = Enumerable.Range(1, history.Count).Select(i => (double)i).ToArray();
        double[] ys = history.Select(h => (double)h.Rating).ToArray();

        // Ulepszamy nagłówek, aby pokazywał kontekst
        AnsiConsole.MarkupLine($"\n[bold yellow]Wykres ratingu dla gracza: {playerName} ({_currentRegion} - {_currentLeaderboardName})[/]");
        AnsiConsole.MarkupLine($"[grey](Pokazywanie ostatnich {history.Count} odczytów)[/]");
        AnsiConsole.MarkupLine("[grey]Oś Y: Rating | Oś X: Kolejne odczyty w czasie[/]");

        // Reszta kodu renderowania wykresu pozostaje bez zmian
        var plt = new Plot(width: 80, height: 20);
        plt.Axis.Pen = new LinePen(SystemLineBrushes.Double, ConsoleColor.White);
        plt.Grid.Pen = new LinePen(SystemLineBrushes.Dotted, ConsoleColor.DarkGray);
        plt.Ticks.IsVisible = true;
        plt.Ticks.Labels.IsVisible = true;
        plt.Ticks.Labels.Color = ConsoleColor.Cyan;
        plt.Ticks.Labels.Format = "F0";
        plt.AddSeries(xs, ys, new PointPen(SystemPointBrushes.Braille, ConsoleColor.Yellow));
        plt.Draw();
        plt.Render();
    }
    private async Task ShowTopLeaderboardAsync()
    {
        // Sprawdzamy, czy użytkownik wybrał jakikolwiek leaderboard
        if (_currentLeaderboardId == 0)
        {
            AnsiConsole.MarkupLine("[red]Najpierw wybierz aktywny leaderboard z menu głównego![/]");
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Pobieranie aktualnego rankingu...[/]");

        // Wywołujemy serwis z zapisanymi w stanie parametrami
        var allPlayers = await _leaderboardService.GetFullLeaderboardAsync(_currentLeaderboardId, _currentRegion);
        var topPlayers = allPlayers.OrderBy(p => p.Rank).Take(25);

        if (!topPlayers.Any())
        {
            AnsiConsole.MarkupLine($"[red]Brak danych w bazie dla [yellow]{_currentRegion} - {_currentLeaderboardName}[/][/]");
            return;
        }

        var table = new Table().Expand().Border(TableBorder.Rounded);
        // Używamy zmiennych stanu, aby tytuł był dynamiczny
        table.Title($"[cyan]TOP 25 Graczy: [yellow]{_currentRegion} - {_currentLeaderboardName}[/][/]");
        table.AddColumn("Rank").AddColumn("BattleTag").AddColumn("Rating");

        foreach (var entry in topPlayers)
        {
            table.AddRow(
                entry.Rank.ToString(),
                entry.BattleTag,
                entry.Rating.ToString()
            );
        }
        AnsiConsole.Write(table);
    }
    private async Task ShowPlayerHistoryAsync()
    {
        // Sprawdzamy, czy użytkownik wybrał leaderboard
        if (_currentLeaderboardId == 0)
        {
            AnsiConsole.MarkupLine("[red]Najpierw wybierz aktywny leaderboard z menu głównego![/]");
            return;
        }

        var battleTag = AnsiConsole.Ask<string>("Wpisz [green]BattleTag[/] gracza, którego chcesz wyszukać:");

        // Wywołujemy serwis z dodatkowymi parametrami
        var playerHistory = await _leaderboardService.GetPlayerHistoryAsync(battleTag, _currentLeaderboardId, _currentRegion);

        if (!playerHistory.Any())
        {
            AnsiConsole.MarkupLine($"[red]Nie znaleziono historii dla gracza '{battleTag}' w [yellow]{_currentRegion} - {_currentLeaderboardName}[/][/]");
            return;
        }

        var table = new Table().Expand().Border(TableBorder.Rounded);
        // Ulepszamy tytuł, aby pokazywał kontekst
        table.Title($"[cyan]Historia dla gracza: {playerHistory.First().BattleTag} ({_currentRegion} - {_currentLeaderboardName})[/]");
        table.AddColumn("Data zapisu").AddColumn("Rank").AddColumn("Rating");

        foreach (var entry in playerHistory)
        {
            table.AddRow(
                entry.ScrapeTimestamp.ToString("yyyy-MM-dd HH:mm"),
                entry.Rank?.ToString() ?? "[grey]N/A[/]",
                entry.Rating?.ToString() ?? "[grey]N/A[/]"
            );
        }
        AnsiConsole.Write(table);
    }
    private async Task ShowDbStatsAsync()
    {
        var stats = await _leaderboardService.GetDbStatsAsync();

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Statystyka");
        table.AddColumn("Wartość");
        table.AddRow("Liczba śledzonych graczy", stats.PlayerCount.ToString());
        table.AddRow("Liczba wpisów w historii", stats.HistoryCount.ToString());

        AnsiConsole.Write(table);
    }
}