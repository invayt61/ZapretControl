using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using Microsoft.Win32;
using ZapretControl.Models;

namespace ZapretControl.Services;

public sealed class DiagnosticsService
{
    private readonly StrategyService _strategies;
    private readonly ZapretService _zapret;

    public DiagnosticsService(StrategyService strategies, ZapretService zapret)
    {
        _strategies = strategies;
        _zapret = zapret;
    }

    public async Task<List<DiagnosticItem>> RunAsync()
    {
        var items = new List<DiagnosticItem>();
        var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Add(items, "Права администратора", admin, admin ? "Приложение может управлять службой и драйвером." : "Перезапустите приложение от имени администратора.");

        var bfe = await ProcessRunner.RunAsync("sc.exe", "query", "BFE");
        var bfeRunning = bfe.Success && bfe.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        Add(items, "Base Filtering Engine", bfeRunning, bfeRunning ? "Системная служба работает." : "Служба BFE не запущена и необходима для фильтрации трафика.");

        var files = File.Exists(_strategies.WinwsPath) && File.Exists(Path.Combine(_strategies.BinPath, "WinDivert64.sys"));
        Add(items, "Файлы ядра и WinDivert", files, files ? "winws.exe и WinDivert64.sys на месте." : "Файлы отсутствуют. Возможно, их удалил антивирус; обновите ядро.");

        var running = _zapret.IsRunning;
        Add(items, "Процесс обхода", running, running ? "winws.exe работает." : "winws.exe не запущен.", running ? "ok" : "warning");

        var serviceInstalled = await _zapret.IsServiceInstalledAsync();
        var serviceRunning = await _zapret.IsServiceRunningAsync();
        Add(items, "Автозапуск Windows", serviceInstalled, serviceInstalled
            ? serviceRunning ? "Служба zapret установлена и работает." : "Служба установлена, но сейчас остановлена."
            : "Служба не установлена; используется ручной запуск.", serviceInstalled && !serviceRunning ? "warning" : "ok");

        using (var proxy = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
        {
            var enabled = Convert.ToInt32(proxy?.GetValue("ProxyEnable", 0)) != 0;
            var server = proxy?.GetValue("ProxyServer")?.ToString();
            Add(items, "Системный прокси", !enabled, enabled ? $"Включён прокси {server}. Убедитесь, что он работает." : "Системный прокси отключён.", enabled ? "warning" : "ok");
        }

        var timestamps = await ProcessRunner.RunAsync("netsh.exe", "interface", "tcp", "show", "global");
        var timestampEnabled = timestamps.Output.Contains("enabled", StringComparison.OrdinalIgnoreCase) || timestamps.Output.Contains("включ", StringComparison.OrdinalIgnoreCase);
        Add(items, "TCP timestamps", timestampEnabled, timestampEnabled ? "Временные метки TCP включены." : "Они будут включены автоматически при следующем запуске.", timestampEnabled ? "ok" : "warning");

        var conflicting = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2" };
        var found = new List<string>();
        foreach (var service in conflicting)
            if ((await ProcessRunner.RunAsync("sc.exe", "query", service)).Success) found.Add(service);
        Add(items, "Конфликтующие обходы", found.Count == 0, found.Count == 0 ? "Известные конфликтующие службы не найдены." : "Найдены: " + string.Join(", ", found), found.Count == 0 ? "ok" : "warning");

        var adguard = Process.GetProcessesByName("AdguardSvc").Length > 0;
        Add(items, "AdGuard", !adguard, adguard ? "AdGuard может конфликтовать с Discord при работе zapret." : "Процесс AdGuard не найден.", adguard ? "warning" : "ok");

        var cyrillic = _strategies.EnginePath.Any(x => x is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        Add(items, "Путь установки", !cyrillic, cyrillic ? "В пути есть кириллица. Если обход не работает, перенесите приложение, например, в C:\\ZapretControl." : "Специальных кириллических символов в пути нет.", cyrillic ? "warning" : "ok");

        var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        var hostsWarning = File.Exists(hostsPath) && File.ReadLines(hostsPath).Any(x => !x.TrimStart().StartsWith("#") && (x.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || x.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)));
        Add(items, "Файл hosts", !hostsWarning, hostsWarning ? "Есть записи YouTube, которые могут мешать подключению." : "Подозрительных записей YouTube не найдено.", hostsWarning ? "warning" : "ok");

        items.Add(await CheckSiteAsync("YouTube", "https://www.youtube.com/generate_204"));
        items.Add(await CheckSiteAsync("Discord", "https://discord.com/api/v10/gateway"));
        return items;
    }

    public static async Task<(bool Success, string Text)> CheckConnectionAsync(string name)
    {
        var url = name == "YouTube" ? "https://www.youtube.com/generate_204" : "https://discord.com/api/v10/gateway";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await client.GetAsync(url);
            return (response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "Доступен" : $"HTTP {(int)response.StatusCode}");
        }
        catch { return (false, "Нет ответа"); }
    }

    private static async Task<DiagnosticItem> CheckSiteAsync(string name, string url)
    {
        var result = await CheckConnectionAsync(name);
        return new DiagnosticItem { Name = "Доступность " + name, Status = result.Success ? "ok" : "warning", Details = result.Success ? result.Text + " по HTTPS." : result.Text + ". Проверьте стратегию и Secure DNS." };
    }

    private static void Add(List<DiagnosticItem> items, string name, bool success, string details, string? status = null) =>
        items.Add(new DiagnosticItem { Name = name, Status = SuccessStatus(success, status), Details = details });

    private static string SuccessStatus(bool success, string? status) => status ?? (success ? "ok" : "error");
}
