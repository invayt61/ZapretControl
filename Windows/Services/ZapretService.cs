using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using ZapretControl.Models;

namespace ZapretControl.Services;

public sealed class ZapretService
{
    private readonly StrategyService _strategies;
    private readonly LogService _log;

    public ZapretService(StrategyService strategies, LogService log)
    {
        _strategies = strategies;
        _log = log;
    }

    public bool IsRunning => Process.GetProcessesByName("winws").Length > 0;

    public async Task<bool> IsServiceInstalledAsync()
    {
        var result = await ProcessRunner.RunAsync("sc.exe", "query", "zapret");
        return result.ExitCode == 0;
    }

    public async Task<bool> IsServiceRunningAsync()
    {
        var result = await ProcessRunner.RunAsync("sc.exe", "query", "zapret");
        return result.ExitCode == 0 && result.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetServiceStrategy()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret");
        return key?.GetValue("zapret-discord-youtube") as string;
    }

    public async Task StartAsync(StrategyInfo strategy, AppSettings settings)
    {
        if (!File.Exists(_strategies.WinwsPath)) throw new FileNotFoundException("winws.exe не найден. Обновите или переустановите ядро.", _strategies.WinwsPath);
        _strategies.ApplySettings(settings);
        await StopAsync();
        await ProcessRunner.RunAsync("netsh.exe", "interface", "tcp", "set", "global", "timestamps=enabled");
        var arguments = _strategies.BuildArguments(strategy, settings);

        if (settings.AutoStart)
        {
            await DeleteServiceAsync();
            var commandLine = $"\"{_strategies.WinwsPath}\" {arguments}";
            var create = await ProcessRunner.RunAsync("sc.exe", "create", "zapret", "binPath=", commandLine, "DisplayName=", "Zapret DPI bypass", "start=", "auto");
            if (!create.Success) throw new InvalidOperationException("Не удалось создать службу zapret: " + create.Output);
            await ProcessRunner.RunAsync("sc.exe", "description", "zapret", "Zapret Discord YouTube — управляется Zapret Control");
            using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\zapret"))
                key.SetValue("zapret-discord-youtube", Path.GetFileNameWithoutExtension(strategy.FileName));
            var start = await ProcessRunner.RunAsync("sc.exe", "start", "zapret");
            if (!start.Success && !start.Output.Contains("1056", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Служба создана, но не запустилась: " + start.Output);
            await Task.Delay(1000);
            if (!await IsServiceRunningAsync()) throw new InvalidOperationException("Служба zapret остановилась после запуска. Проверьте диагностику и журнал Windows.");
            _log.Info($"Служба zapret запущена: {strategy.Name}.");
        }
        else
        {
            await DeleteServiceAsync();
            var startInfo = new ProcessStartInfo
            {
                FileName = _strategies.WinwsPath,
                Arguments = arguments,
                WorkingDirectory = _strategies.BinPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить winws.exe.");
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _log.Info("winws: " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _log.Error("winws: " + e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.Delay(1200);
            if (process.HasExited) throw new InvalidOperationException($"winws.exe завершился с кодом {process.ExitCode}. Подробности в журнале.");
            _log.Info($"Обход запущен: {strategy.Name}.");
        }
    }

    public async Task StopAsync()
    {
        if (await IsServiceInstalledAsync())
        {
            await ProcessRunner.RunAsync("sc.exe", "stop", "zapret");
            for (var i = 0; i < 20 && IsRunning; i++) await Task.Delay(250);
            _log.Info("Служба zapret остановлена.");
        }

        foreach (var process in Process.GetProcessesByName("winws"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.Equals(path, _strategies.WinwsPath, StringComparison.OrdinalIgnoreCase)) continue;
                process.Kill(true);
                await process.WaitForExitAsync();
            }
            catch (Exception ex) { _log.Error("Не удалось остановить winws.exe: " + ex.Message); }
            finally { process.Dispose(); }
        }
        _log.Info("Обход остановлен.");
    }

    public async Task DeleteServiceAsync()
    {
        if (!await IsServiceInstalledAsync()) return;
        await ProcessRunner.RunAsync("sc.exe", "stop", "zapret");
        var result = await ProcessRunner.RunAsync("sc.exe", "delete", "zapret");
        if (!result.Success && !result.Output.Contains("1060", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Не удалось удалить службу zapret: " + result.Output);
        _log.Info("Автозапуск через службу отключён.");
    }
}
