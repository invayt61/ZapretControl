using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZapretControl.Models;

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
    public string LogPath => Path.Combine(DataPath, "proxy.log");
    public string VersionPath => Path.Combine(RootPath, "version.txt");
    public string IntegratedMarkerPath => Path.Combine(RootPath, "integrated-engine.marker");
    public bool IsInstalled => File.Exists(ExecutablePath);
    public bool IsIntegrated => File.Exists(IntegratedMarkerPath);

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

    public async Task StartAsync(AppSettings settings)
    {
        if (IsRunning) return;
        if (!IsInstalled || !IsIntegrated) throw new FileNotFoundException("Интегрированный Telegram-прокси не установлен.", ExecutablePath);
        PrepareConfiguration(settings.TelegramProxyPort);
        await StopOtherInstancesAsync();
        EnsurePortAvailable(settings.TelegramProxyPort);
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        AddConfigurationArguments(startInfo);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить Telegram-прокси.");
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _log.Info("Telegram: " + e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _log.Info("Telegram: " + e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await Task.Delay(1800);
        if (process.HasExited)
            throw new InvalidOperationException($"Telegram-прокси завершился с кодом {process.ExitCode}. Проверьте порт {settings.TelegramProxyPort} и журнал.");
        _log.Info($"Telegram-прокси запущен: 127.0.0.1:{settings.TelegramProxyPort}.");
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

    private async Task StopOtherInstancesAsync()
    {
        foreach (var process in Process.GetProcessesByName("TgWsProxy"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (string.Equals(path, ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;
                process.Kill(true);
                await process.WaitForExitAsync();
                _log.Info("Предыдущая копия Telegram-прокси остановлена.");
            }
            catch (Exception ex) { _log.Error("Не удалось остановить предыдущую копию Telegram-прокси: " + ex.Message); }
            finally { process.Dispose(); }
        }
    }

    private static void EnsurePortAvailable(int port)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (System.Net.Sockets.SocketException ex) when (ex.ErrorCode == 10048)
        {
            throw new InvalidOperationException($"Порт {port} уже занят другой программой. Закройте её или выберите другой порт в настройках Telegram-прокси.");
        }
        finally { listener.Stop(); }
    }

    public async Task ApplyAutoStartAsync(bool enabled)
    {
        const string taskName = "ZapretControl";
        if (!enabled)
        {
            await ProcessRunner.RunAsync("schtasks.exe", "/delete", "/tn", taskName, "/f");
            return;
        }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь ZapretControl.exe.");
        var result = await ProcessRunner.RunAsync("schtasks.exe", "/create", "/tn", taskName, "/sc", "onlogon", "/rl", "highest", "/tr", $"\"{executable}\" --hidden", "/f");
        if (!result.Success) throw new InvalidOperationException("Не удалось настроить автозапуск Telegram-прокси: " + result.Output);
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

    public void PrepareConfiguration(int port = 1443)
    {
        Directory.CreateDirectory(DataPath);
        JsonObject config;
        if (File.Exists(ConfigPath))
        {
            config = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject
                ?? throw new InvalidDataException("Некорректный config.json Telegram-прокси.");
        }
        else config = new JsonObject();
        config["host"] = config["host"]?.GetValue<string>() ?? "127.0.0.1";
        config["port"] = port;
        if (string.IsNullOrWhiteSpace(config["secret"]?.GetValue<string>()))
            config["secret"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        config["check_updates"] = false;
        var temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, config.ToJsonString(JsonOptions));
        File.Move(temporary, ConfigPath, true);
    }

    private void AddConfigurationArguments(ProcessStartInfo startInfo)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        var root = document.RootElement;
        void Add(string name, string value) { startInfo.ArgumentList.Add(name); startInfo.ArgumentList.Add(value); }
        Add("--host", root.GetProperty("host").GetString() ?? "127.0.0.1");
        Add("--port", root.GetProperty("port").GetInt32().ToString());
        Add("--secret", root.GetProperty("secret").GetString()!);
        if (root.TryGetProperty("dc_ip", out var dcIp) && dcIp.ValueKind == JsonValueKind.Array)
        {
            if (dcIp.GetArrayLength() == 0) startInfo.ArgumentList.Add("--dc-ip");
            foreach (var item in dcIp.EnumerateArray()) Add("--dc-ip", item.GetString() ?? "");
        }
        if (root.TryGetProperty("fake_tls_domain", out var fakeTls) && !string.IsNullOrWhiteSpace(fakeTls.GetString())) Add("--fake-tls-domain", fakeTls.GetString()!);
        if (root.TryGetProperty("buf_kb", out var buffer)) Add("--buf-kb", buffer.GetInt32().ToString());
        if (root.TryGetProperty("pool_size", out var pool)) Add("--pool-size", pool.GetInt32().ToString());
        if (root.TryGetProperty("cfproxy", out var cfProxy) && !cfProxy.GetBoolean()) startInfo.ArgumentList.Add("--no-cfproxy");
        if (root.TryGetProperty("h2", out var h2) && !h2.GetBoolean()) startInfo.ArgumentList.Add("--no-h2");
        if (root.TryGetProperty("no_secure", out var noSecure) && noSecure.GetBoolean()) startInfo.ArgumentList.Add("--no-secure");
        if (root.TryGetProperty("force_test_dc", out var testDc) && testDc.GetBoolean()) startInfo.ArgumentList.Add("--force-test-dc");
        if (root.TryGetProperty("verbose", out var verbose) && verbose.GetBoolean()) startInfo.ArgumentList.Add("--verbose");
        AddDomains(startInfo, root, "cfproxy_user_domain", "cfproxy_user_domain_enabled", "--cfproxy-domain");
        AddDomains(startInfo, root, "cfproxy_worker_domain", "cfproxy_worker_enabled", "--cfproxy-worker-domain");
        Add("--log-file", LogPath);
        if (root.TryGetProperty("log_max_mb", out var logMax)) Add("--log-max-mb", logMax.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AddDomains(ProcessStartInfo startInfo, JsonElement root, string property, string enabledProperty, string argument)
    {
        if (!root.TryGetProperty(property, out var domains) || domains.ValueKind != JsonValueKind.Array) return;
        if (root.TryGetProperty(enabledProperty, out var enabled) && !enabled.GetBoolean()) return;
        foreach (var domain in domains.EnumerateArray())
        {
            var value = domain.GetString();
            if (string.IsNullOrWhiteSpace(value)) continue;
            startInfo.ArgumentList.Add(argument);
            startInfo.ArgumentList.Add(value);
        }
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
