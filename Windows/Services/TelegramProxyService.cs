using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZapretControl.Services;

public sealed class TelegramProxyService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly LogService _log;

    public TelegramProxyService(LogService log, string? rootPath = null)
    {
        _log = log;
        RootPath = rootPath ?? Path.Combine(AppContext.BaseDirectory, "telegram-proxy");
    }

    public string RootPath { get; }
    public string ExecutablePath => Path.Combine(RootPath, "TgWsProxy.exe");
    public string DataPath => Path.Combine(RootPath, "TgWsProxy_data");
    public string ConfigPath => Path.Combine(DataPath, "config.json");
    public string VersionPath => Path.Combine(RootPath, "version.txt");
    public bool IsInstalled => File.Exists(ExecutablePath);

    public bool IsRunning
    {
        get
        {
            foreach (var process in FindProcesses())
            {
                var running = !process.HasExited;
                process.Dispose();
                if (running) return true;
            }
            return false;
        }
    }

    public string GetInstalledVersion()
    {
        try
        {
            if (File.Exists(VersionPath)) return File.ReadAllText(VersionPath).Trim();
        }
        catch { }
        return IsInstalled ? "неизвестно" : "не установлен";
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        if (!IsInstalled) throw new FileNotFoundException("Telegram-прокси не установлен.", ExecutablePath);
        PrepareConfiguration();
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = RootPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--portable");
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить TgWsProxy.exe.");
        await Task.Delay(1800);
        if (process.HasExited)
            throw new InvalidOperationException($"TgWsProxy.exe завершился с кодом {process.ExitCode}. Закройте другую копию прокси и проверьте proxy.log.");
        _log.Info("Telegram-прокси запущен: 127.0.0.1:1443.");
    }

    public async Task StopAsync()
    {
        var stopped = false;
        foreach (var process in FindProcesses())
        {
            try
            {
                process.Kill(true);
                await process.WaitForExitAsync();
                stopped = true;
            }
            catch (Exception ex) { _log.Error("Не удалось остановить Telegram-прокси: " + ex.Message); }
            finally { process.Dispose(); }
        }
        if (stopped) _log.Info("Telegram-прокси остановлен.");
    }

    public string GetTelegramUrl()
    {
        if (!File.Exists(ConfigPath)) throw new InvalidOperationException("Настройки прокси ещё не созданы. Подождите несколько секунд после запуска.");
        using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        var root = document.RootElement;
        var host = root.TryGetProperty("host", out var hostElement) ? hostElement.GetString() ?? "127.0.0.1" : "127.0.0.1";
        var port = root.TryGetProperty("port", out var portElement) ? portElement.GetInt32() : 1443;
        var secret = root.TryGetProperty("secret", out var secretElement) ? secretElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidDataException("В настройках прокси не найден секрет.");
        if (host is "0.0.0.0" or "::") host = "127.0.0.1";
        if (host.Contains(':') && !host.StartsWith('[')) host = "[" + host + "]";
        return $"tg://proxy?server={Uri.EscapeDataString(host)}&port={port}&secret=dd{secret}";
    }

    public void PrepareConfiguration()
    {
        Directory.CreateDirectory(DataPath);
        JsonObject config;
        if (File.Exists(ConfigPath))
        {
            config = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject
                ?? throw new InvalidDataException("Некорректный config.json Telegram-прокси.");
        }
        else config = new JsonObject();
        config["check_updates"] = false;
        var temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, config.ToJsonString(JsonOptions));
        File.Move(temporary, ConfigPath, true);
    }

    private IEnumerable<Process> FindProcesses()
    {
        foreach (var process in Process.GetProcessesByName("TgWsProxy"))
        {
            string? path = null;
            try { path = process.MainModule?.FileName; }
            catch { process.Dispose(); }
            if (path == null) continue;
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(ExecutablePath), StringComparison.OrdinalIgnoreCase)) yield return process;
            else process.Dispose();
        }
    }
}
