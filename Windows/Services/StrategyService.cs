using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using ZapretControl.Models;

namespace ZapretControl.Services;

public sealed class StrategyService
{
    public const string DisabledIpSetMarker = "203.0.113.113/32";
    public string EnginePath { get; }
    public string BinPath => Path.Combine(EnginePath, "bin");
    public string ListsPath => Path.Combine(EnginePath, "lists");
    public string WinwsPath => Path.Combine(BinPath, "winws.exe");

    public StrategyService(string? enginePath = null)
    {
        EnginePath = enginePath ?? Path.Combine(AppContext.BaseDirectory, "engine");
    }

    public List<StrategyInfo> LoadStrategies()
    {
        if (!Directory.Exists(EnginePath)) return new List<StrategyInfo>();
        return Directory.GetFiles(EnginePath, "general*.bat")
            .Select(path => new StrategyInfo
            {
                FileName = Path.GetFileName(path),
                Name = Path.GetFileNameWithoutExtension(path),
                Description = Describe(Path.GetFileNameWithoutExtension(path))
            })
            .OrderBy(x => x.IsRecommended ? 0 : 1)
            .ThenBy(x => x.Name, new NaturalStringComparer())
            .ToList();
    }

    public string GetCoreVersion()
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(EnginePath, "service.bat"));
            return Regex.Match(text, @"LOCAL_VERSION=([^\r\n]+)").Groups[1].Value.Trim().Trim('"');
        }
        catch { return "неизвестно"; }
    }

    public string BuildArguments(StrategyInfo strategy, AppSettings settings)
    {
        var file = Path.Combine(EnginePath, strategy.FileName);
        var text = File.ReadAllText(file, Encoding.UTF8);
        text = Regex.Replace(text, @"\^\s*\r?\n", " ");
        var index = text.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
        if (index < 0) throw new InvalidDataException($"В {strategy.FileName} не найдена команда winws.exe.");

        var arguments = text[(index + "winws.exe".Length)..]
            .Replace("%BIN%", BinPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
            .Replace("%LISTS%", ListsPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilterTCP%", GamePorts(settings, true), StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilterUDP%", GamePorts(settings, false), StringComparison.OrdinalIgnoreCase)
            .Replace("%~dp0", EnginePath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
            .Trim().TrimEnd('^').Trim();
        arguments = arguments.TrimStart().TrimStart('"').TrimStart();

        if (!arguments.StartsWith("--", StringComparison.Ordinal))
            throw new InvalidDataException($"Не удалось разобрать аргументы {strategy.FileName}.");
        if (Regex.IsMatch(arguments, "%[A-Za-z]+%"))
            throw new InvalidDataException($"В {strategy.FileName} остались неизвестные переменные.");
        return arguments;
    }

    public void EnsureUserLists()
    {
        Directory.CreateDirectory(ListsPath);
        Directory.CreateDirectory(Path.Combine(EnginePath, "utils"));
        EnsureFile("list-general-user.txt", "# Домены для обхода, по одному на строку\n# Не оставляйте этот файл пустым\ndomain.example.abc\n");
        EnsureFile("list-exclude-user.txt", "# Домены, которые не нужно обрабатывать\ndomain.example.abc\n");
        EnsureFile("ipset-exclude-user.txt", "# IP и подсети, которые не нужно обрабатывать\n203.0.113.113/32\n");
    }

    private void EnsureFile(string name, string content)
    {
        var path = Path.Combine(ListsPath, name);
        if (!File.Exists(path)) File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public void ApplySettings(AppSettings settings)
    {
        EnsureUserLists();
        ValidatePortRange(settings.GameTcpPorts);
        ValidatePortRange(settings.GameUdpPorts);
        var flag = Path.Combine(EnginePath, "utils", "game_filter.enabled");
        if (settings.GameFilter.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(flag)) File.Delete(flag);
        }
        else
        {
            var lines = new List<string> { $"mode={settings.GameFilter}" };
            if (settings.GameFilter is "all" or "tcp") lines.Add($"tcp={settings.GameTcpPorts}");
            if (settings.GameFilter is "all" or "udp") lines.Add($"udp={settings.GameUdpPorts}");
            File.WriteAllLines(flag, lines, new UTF8Encoding(false));
        }
        ApplyIpSetMode(settings.IpSetMode);
    }

    public string GetIpSetMode()
    {
        var path = Path.Combine(ListsPath, "ipset-all.txt");
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return "any";
        return File.ReadLines(path).Any(x => x.Trim().Equals(DisabledIpSetMarker, StringComparison.OrdinalIgnoreCase))
            ? "none" : "loaded";
    }

    private void ApplyIpSetMode(string mode)
    {
        var path = Path.Combine(ListsPath, "ipset-all.txt");
        var backup = path + ".backup";
        var current = GetIpSetMode();
        if (current == mode) return;

        if (current == "loaded" && mode is "none" or "any") File.Copy(path, backup, true);
        if (mode == "none") File.WriteAllText(path, DisabledIpSetMarker + Environment.NewLine);
        else if (mode == "any") File.WriteAllText(path, "");
        else if (mode == "loaded")
        {
            if (File.Exists(backup)) File.Copy(backup, path, true);
            else if (current is "none" or "any")
                throw new InvalidOperationException("Резервная копия IPSet не найдена. Сначала обновите список IPSet.");
        }
    }

    public async Task UpdateIpSetAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var content = await client.GetStringAsync("https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt", cancellationToken);
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("GitHub вернул пустой список IPSet.");
        Directory.CreateDirectory(ListsPath);
        var path = Path.Combine(ListsPath, "ipset-all.txt");
        await File.WriteAllTextAsync(path + ".tmp", content, cancellationToken);
        File.Move(path + ".tmp", path, true);
    }

    public static void ValidatePortRange(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Укажите диапазон портов.");
        foreach (var item in value.Replace(" ", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(item, @"^(\d{1,5})(?:-(\d{1,5}))?$");
            if (!match.Success) throw new ArgumentException($"Некорректный диапазон портов: {item}");
            var start = int.Parse(match.Groups[1].Value);
            var end = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : start;
            if (start < 1 || end > 65535 || start > end) throw new ArgumentException($"Недопустимый диапазон портов: {item}");
        }
    }

    private static string GamePorts(AppSettings settings, bool tcp)
    {
        var enabled = settings.GameFilter == "all" || settings.GameFilter == (tcp ? "tcp" : "udp");
        return enabled ? (tcp ? settings.GameTcpPorts : settings.GameUdpPorts) : "12";
    }

    private static string Describe(string name)
    {
        if (name == "general") return "Базовая стратегия. Рекомендуется начать с неё.";
        if (name.Contains("FAKE TLS AUTO")) return "Автоматический подбор TLS-фейка. Попробуйте, если базовые варианты не работают.";
        if (name.Contains("SIMPLE FAKE")) return "Упрощённая подмена пакетов с альтернативными параметрами.";
        if (name.Contains("EXP")) return "Экспериментальная комбинация параметров обхода.";
        return "Альтернативный набор параметров DPI. Эффективность зависит от провайдера.";
    }

    private sealed class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) => LogicalCompare(x ?? "", y ?? "");
        private static int LogicalCompare(string x, string y)
        {
            var partsX = Regex.Split(x, @"(\d+)");
            var partsY = Regex.Split(y, @"(\d+)");
            for (var i = 0; i < Math.Min(partsX.Length, partsY.Length); i++)
            {
                var result = int.TryParse(partsX[i], out var nx) && int.TryParse(partsY[i], out var ny)
                    ? nx.CompareTo(ny) : string.Compare(partsX[i], partsY[i], StringComparison.OrdinalIgnoreCase);
                if (result != 0) return result;
            }
            return partsX.Length.CompareTo(partsY.Length);
        }
    }
}
