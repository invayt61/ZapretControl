using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using ZapretControl.Models;
using ZapretControl.Services;

namespace ZapretControl;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test") || e.Args.Contains("--verify-update"))
        {
            Console.OutputEncoding = Encoding.UTF8;
            var exitCode = await RunSelfTestAsync(e.Args.Contains("--verify-update"));
            Shutdown(exitCode);
            return;
        }
        if (e.Args.Contains("--verify-ui"))
        {
            Console.OutputEncoding = Encoding.UTF8;
            Shutdown(RunUiVerification());
            return;
        }
        if (e.Args.Contains("--verify-tray"))
        {
            Console.OutputEncoding = Encoding.UTF8;
            var exitCode = await RunTrayVerification();
            Shutdown(exitCode);
            return;
        }

        _mutex = new Mutex(true, "Local\\ZapretControl.SingleInstance", out var created);
        if (!created)
        {
            System.Windows.MessageBox.Show("Zapret Control уже запущен.", "Zapret Control", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Contains("--hidden")) await window.InitializeAsync();
        else window.Show();
    }

    private static async Task<int> RunSelfTestAsync(bool verifyUpdate)
    {
        var temporary = "";
        try
        {
            var service = new StrategyService();
            var strategies = service.LoadStrategies();
            if (strategies.Count == 0) throw new InvalidDataException("Стратегии не найдены.");
            var settings = new AppSettings();
            foreach (var strategy in strategies)
            {
                var arguments = service.BuildArguments(strategy, settings);
                Console.WriteLine($"OK {strategy.FileName}: {arguments.Length} символов аргументов");
            }
            Console.WriteLine($"Ядро: {service.GetCoreVersion()}; стратегий: {strategies.Count}");

            var updates = new UpdateService();
            var release = await updates.CheckAsync();
            Console.WriteLine($"Zapret GitHub: {release.Version}; SHA-256: {release.Sha256}; ZIP: {release.Size} байт");
            if (verifyUpdate)
            {
                temporary = Path.Combine(Path.GetTempPath(), "ZapretControl-verify-" + Guid.NewGuid().ToString("N"));
                var engine = Path.Combine(temporary, "engine");
                Directory.CreateDirectory(Path.Combine(engine, "lists"));
                await File.WriteAllTextAsync(Path.Combine(engine, "lists", "list-general-user.txt"), "preserved.example\n");
                await updates.InstallAsync(release, engine);
                if (await File.ReadAllTextAsync(Path.Combine(engine, "lists", "list-general-user.txt")) != "preserved.example\n")
                    throw new InvalidDataException("Пользовательский список не сохранился при обновлении.");
                var installed = new StrategyService(engine);
                foreach (var strategy in installed.LoadStrategies()) _ = installed.BuildArguments(strategy, settings);
                if (installed.GetCoreVersion() != release.Version) throw new InvalidDataException("Версия установленного ядра не совпадает с релизом.");
                Console.WriteLine("Обновление zapret проверено: скачивание, хэш, безопасная распаковка и все стратегии — успешно.");
            }

            var appUpdates = new AppUpdateService();
            var appRelease = await appUpdates.CheckAsync();
            Console.WriteLine($"Zapret Control GitHub: {appRelease.Version}; SHA-256: {appRelease.Sha256}; ZIP: {appRelease.Size} байт");
            if (verifyUpdate)
            {
                var prepared = await appUpdates.PrepareAsync(appRelease);
                try
                {
                    if (!File.Exists(prepared.ExecutablePath)) throw new InvalidDataException("EXE обновления приложения не подготовлен.");
                    Console.WriteLine("Обновление приложения проверено: скачивание, SHA-256 и извлечение EXE — успешно.");
                }
                finally { try { Directory.Delete(prepared.WorkDirectory, true); } catch { } }
            }

            var proxyRoot = Path.Combine(string.IsNullOrEmpty(temporary) ? Path.GetTempPath() : temporary, "telegram-proxy");
            var proxy = new TelegramProxyService(new LogService(), proxyRoot);
            var proxyUpdates = new TelegramProxyUpdateService(proxy);
            var proxyRelease = await proxyUpdates.CheckAsync();
            Console.WriteLine($"TG WS Proxy GitHub: {proxyRelease.Version}; SHA-256: {proxyRelease.Sha256}; EXE: {proxyRelease.Size} байт");
            if (verifyUpdate)
            {
                await proxyUpdates.InstallAsync(proxyRelease);
                if (proxy.GetInstalledVersion() != proxyRelease.Version) throw new InvalidDataException("Версия Telegram-прокси не совпадает с релизом.");
                proxy.PrepareConfiguration();
                using var config = JsonDocument.Parse(await File.ReadAllTextAsync(proxy.ConfigPath));
                if (!config.RootElement.TryGetProperty("check_updates", out var checkUpdates) || checkUpdates.GetBoolean())
                    throw new InvalidDataException("Встроенная проверка обновлений Telegram-прокси не отключена.");
                const int testPort = 21443;
                const string testSecret = "00112233445566778899aabbccddeeff";
                settings.TelegramProxyPort = testPort;
                await File.WriteAllTextAsync(proxy.ConfigPath, $"{{\"host\":\"127.0.0.1\",\"port\":{testPort},\"secret\":\"{testSecret}\",\"check_updates\":false}}");
                await File.WriteAllTextAsync(Path.Combine(proxy.DataPath, ".first_run_done_mtproto"), "");
                try
                {
                    await proxy.StartAsync(settings);
                    if (!proxy.IsRunning) throw new InvalidOperationException("Процесс Telegram-прокси не найден после запуска.");
                    using (var connection = new System.Net.Sockets.TcpClient()) await connection.ConnectAsync("127.0.0.1", testPort);
                    var expectedUrl = $"tg://proxy?server=127.0.0.1&port={testPort}&secret=dd{testSecret}";
                    if (proxy.GetTelegramUrl() != expectedUrl) throw new InvalidDataException("Ссылка для Telegram сформирована неверно.");
                }
                finally { await proxy.StopAsync(); }
                if (proxy.IsRunning) throw new InvalidOperationException("Telegram-прокси не остановился.");

                var legacyRoot = Path.Combine(temporary, "legacy-telegram-proxy");
                Directory.CreateDirectory(legacyRoot);
                File.Copy(proxy.ExecutablePath, Path.Combine(legacyRoot, "TgWsProxy.exe"));
                File.Copy(proxy.IntegratedMarkerPath, Path.Combine(legacyRoot, "integrated-engine.marker"));
                var legacyProxy = new TelegramProxyService(new LogService(), legacyRoot);
                try
                {
                    await legacyProxy.StartAsync(settings);
                    if (!legacyProxy.IsRunning) throw new InvalidOperationException("Старая копия Telegram-прокси не запустилась для проверки.");
                    await proxy.StartAsync(settings);
                    if (legacyProxy.IsRunning) throw new InvalidOperationException("Старая копия Telegram-прокси не была остановлена.");
                    if (!proxy.IsRunning) throw new InvalidOperationException("Новый Telegram-прокси не запустился после остановки старой копии.");
                }
                finally { await proxy.StopAsync(); await legacyProxy.StopAsync(); }
                Console.WriteLine("Telegram-прокси проверен: скачивание, SHA-256, запуск, порт, остановка и замена старой копии — успешно.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { if (!string.IsNullOrEmpty(temporary) && Directory.Exists(temporary)) Directory.Delete(temporary, true); } catch { }
        }
    }

    private static int RunUiVerification()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ZapretControl-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.CreateDirectory(directory);
            foreach (var size in new[] { (Width: 1180, Height: 780), (Width: 1040, Height: 680) })
            {
                var window = new MainWindow(false, false) { Width = size.Width, Height = size.Height };
                var viewModel = (ViewModels.MainViewModel)window.DataContext;
                window.Show();
                var pages = size.Width == 1180 ? Enumerable.Range(0, 6) : new[] { 0 };
                foreach (var page in pages)
                {
                    viewModel.CurrentPage = page;
                    window.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(size.Width, size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"page-{page}-{size.Width}.png"));
                    encoder.Save(stream);
                }
                window.Close();
            }
            Console.WriteLine(directory);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> RunTrayVerification()
    {
        MainWindow? window = null;
        try
        {
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window = new MainWindow(false, true);
            window.Show();
            window.UpdateLayout();
            window.Close();
            if (window.IsVisible) throw new InvalidOperationException("Окно не скрылось в трей.");
            window.RestoreFromTray();
            if (!window.IsVisible) throw new InvalidOperationException("Окно не восстановилось из трея.");
            window.ExitFromTray();
            await Task.Delay(150);
            if (window.IsLoaded || window.IsVisible) throw new InvalidOperationException("Полный выход не закрыл окно.");
            Console.WriteLine("Трей проверен: скрытие, восстановление и полный выход — успешно.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (window is { IsLoaded: true }) window.ExitFromTray();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
