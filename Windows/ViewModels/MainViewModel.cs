using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ZapretControl.Models;
using ZapretControl.Services;

namespace ZapretControl.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService = new();
    private readonly StrategyService _strategies = new();
    private readonly LogService _log = new();
    private readonly ZapretService _zapret;
    private readonly UpdateService _updates = new();
    private readonly TelegramProxyService _telegramProxy;
    private readonly TelegramProxyUpdateService _telegramProxyUpdates;
    private readonly DiagnosticsService _diagnostics;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _updateTimer;
    private ReleaseInfo? _latestRelease;
    private ReleaseInfo? _latestTelegramProxyRelease;
    private StrategyInfo? _selectedStrategy;
    private UserListDefinition? _selectedList;
    private string _searchText = "";
    private string _listContent = "";
    private int _currentPage;
    private bool _isBusy;
    private string _busyText = "";
    private bool _isRunning;
    private bool _telegramProxyIsRunning;
    private string _currentVersion;
    private string _latestVersion = "—";
    private string _releaseNotes = "Нажмите «Проверить обновления», чтобы получить информацию о последнем релизе.";
    private string _updateStatus = "Обновления ещё не проверялись.";
    private double _updateProgress;
    private bool _hasUpdate;
    private string _telegramProxyCurrentVersion;
    private string _telegramProxyLatestVersion = "—";
    private string _telegramProxyReleaseNotes = "Нажмите «Проверить обновления», чтобы получить информацию о последнем релизе Telegram-прокси.";
    private string _telegramProxyUpdateStatus = "Прокси установится автоматически при первом запуске.";
    private double _telegramProxyUpdateProgress;
    private bool _hasTelegramProxyUpdate;
    private string _youTubeStatus = "Не проверено";
    private string _discordStatus = "Не проверено";

    public MainViewModel()
    {
        Settings = _settingsService.Load();
        _zapret = new ZapretService(_strategies, _log);
        _telegramProxy = new TelegramProxyService(_log);
        _telegramProxyUpdates = new TelegramProxyUpdateService(_telegramProxy);
        _diagnostics = new DiagnosticsService(_strategies, _zapret);
        _currentVersion = _strategies.GetCoreVersion();
        _telegramProxyCurrentVersion = _telegramProxy.GetInstalledVersion();
        Strategies = new ObservableCollection<StrategyInfo>(_strategies.LoadStrategies());
        foreach (var strategy in Strategies) FilteredStrategies.Add(strategy);
        SelectedStrategy = Strategies.FirstOrDefault(x => x.FileName.Equals(Settings.SelectedStrategy, StringComparison.OrdinalIgnoreCase)) ?? Strategies.FirstOrDefault();
        UserLists = new ObservableCollection<UserListDefinition>
        {
            new() { Name = "Домены для обхода", FileName = "list-general-user.txt", Description = "Добавьте домены, которые должны использовать обход. Поддомены учитываются автоматически. Один домен на строку." },
            new() { Name = "Исключённые домены", FileName = "list-exclude-user.txt", Description = "Домены из этого списка не обрабатываются zapret. Полезно, если какой-то сайт стал работать хуже." },
            new() { Name = "Исключённые IP и подсети", FileName = "ipset-exclude-user.txt", Description = "IP-адреса и подсети, которые нужно исключить из обработки. Например: 203.0.113.113/32." }
        };
        SelectedList = UserLists[0];

        StartStopCommand = new AsyncRelayCommand(StartStopAsync, () => !IsBusy);
        StartAllCommand = new AsyncRelayCommand(StartAllAsync, () => !IsBusy && (!IsRunning || !TelegramProxyIsRunning));
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => !IsBusy && IsRunning);
        ApplyStrategyCommand = new AsyncRelayCommand(ApplyStrategyAsync, () => !IsBusy && SelectedStrategy != null);
        TelegramProxyStartStopCommand = new AsyncRelayCommand(TelegramProxyStartStopAsync, () => !IsBusy);
        OpenInTelegramCommand = new RelayCommand(OpenInTelegram, () => !IsBusy && TelegramProxyIsRunning);
        CheckUpdatesCommand = new AsyncRelayCommand(() => CheckUpdatesAsync(false), () => !IsBusy);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => !IsBusy && HasUpdate);
        CheckTelegramProxyUpdatesCommand = new AsyncRelayCommand(() => CheckTelegramProxyUpdatesAsync(false), () => !IsBusy);
        InstallTelegramProxyUpdateCommand = new AsyncRelayCommand(InstallTelegramProxyUpdateAsync, () => !IsBusy && HasTelegramProxyUpdate);
        RunDiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync, () => !IsBusy);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        SaveUpdatePreferencesCommand = new RelayCommand(() =>
        {
            _settingsService.Save(Settings);
            _log.Info("Настройки автоматических обновлений сохранены.");
        });
        SaveListCommand = new AsyncRelayCommand(SaveListAsync, () => !IsBusy && SelectedList != null);
        UpdateIpSetCommand = new AsyncRelayCommand(UpdateIpSetAsync, () => !IsBusy);
        CheckConnectionsCommand = new AsyncRelayCommand(CheckConnectionsAsync, () => !IsBusy);
        OpenGitHubCommand = new RelayCommand(() => OpenUrl("https://github.com/Flowseal/zapret-discord-youtube"));
        OpenReleaseCommand = new RelayCommand(() => OpenUrl(_latestRelease?.PageUrl ?? "https://github.com/Flowseal/zapret-discord-youtube/releases/latest"));
        OpenTelegramProxyGitHubCommand = new RelayCommand(() => OpenUrl("https://github.com/Flowseal/tg-ws-proxy"));
        OpenTelegramProxyReleaseCommand = new RelayCommand(() => OpenUrl(_latestTelegramProxyRelease?.PageUrl ?? "https://github.com/Flowseal/tg-ws-proxy/releases/latest"));
        OpenListsFolderCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo("explorer.exe", _strategies.ListsPath) { UseShellExecute = true }));
        ClearLogCommand = new RelayCommand(_log.Entries.Clear);

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => RefreshRunningStatus();
        _statusTimer.Start();
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _updateTimer.Tick += async (_, _) => await AutoUpdateAsync();
        _updateTimer.Start();
    }

    public AppSettings Settings { get; }
    public LogService Log => _log;
    public ObservableCollection<StrategyInfo> Strategies { get; }
    public ObservableCollection<StrategyInfo> FilteredStrategies { get; } = new();
    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = new();
    public ObservableCollection<UserListDefinition> UserLists { get; }

    public AsyncRelayCommand StartStopCommand { get; }
    public AsyncRelayCommand StartAllCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public AsyncRelayCommand ApplyStrategyCommand { get; }
    public AsyncRelayCommand TelegramProxyStartStopCommand { get; }
    public RelayCommand OpenInTelegramCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public AsyncRelayCommand InstallUpdateCommand { get; }
    public AsyncRelayCommand CheckTelegramProxyUpdatesCommand { get; }
    public AsyncRelayCommand InstallTelegramProxyUpdateCommand { get; }
    public AsyncRelayCommand RunDiagnosticsCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public RelayCommand SaveUpdatePreferencesCommand { get; }
    public AsyncRelayCommand SaveListCommand { get; }
    public AsyncRelayCommand UpdateIpSetCommand { get; }
    public AsyncRelayCommand CheckConnectionsCommand { get; }
    public RelayCommand OpenGitHubCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }
    public RelayCommand OpenTelegramProxyGitHubCommand { get; }
    public RelayCommand OpenTelegramProxyReleaseCommand { get; }
    public RelayCommand OpenListsFolderCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public int CurrentPage { get => _currentPage; set => SetProperty(ref _currentPage, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RefreshCommands(); } }
    public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }
    public bool IsRunning { get => _isRunning; private set { if (SetProperty(ref _isRunning, value)) { OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusDescription)); OnPropertyChanged(nameof(StartButtonText)); RefreshCommands(); } } }
    public string StatusText => IsRunning ? "Обход активен" : "Обход остановлен";
    public string StatusDescription => IsRunning ? "Трафик обрабатывается winws.exe" : "Нажмите кнопку, чтобы запустить выбранную стратегию";
    public string StartButtonText => IsRunning ? "Остановить обход" : "Запустить обход";
    public bool TelegramProxyIsRunning
    {
        get => _telegramProxyIsRunning;
        private set
        {
            if (!SetProperty(ref _telegramProxyIsRunning, value)) return;
            OnPropertyChanged(nameof(TelegramProxyStatusText));
            OnPropertyChanged(nameof(TelegramProxyShortStatusText));
            OnPropertyChanged(nameof(TelegramProxyStatusDescription));
            OnPropertyChanged(nameof(TelegramProxyStartButtonText));
            RefreshCommands();
        }
    }
    public string TelegramProxyStatusText => TelegramProxyIsRunning ? "Telegram-прокси активен" : _telegramProxy.IsInstalled ? "Telegram-прокси остановлен" : "Telegram-прокси не установлен";
    public string TelegramProxyShortStatusText => TelegramProxyIsRunning ? "Прокси активен" : _telegramProxy.IsInstalled ? "Прокси остановлен" : "Прокси не установлен";
    public string TelegramProxyStatusDescription => TelegramProxyIsRunning ? $"MTProto · 127.0.0.1:{Settings.TelegramProxyPort} · внутри Zapret Control" : "Без отдельного окна и второго значка в трее";
    public string TelegramProxyStartButtonText => TelegramProxyIsRunning ? "Остановить прокси" : !_telegramProxy.IsInstalled ? "Установить и запустить" : !_telegramProxy.IsIntegrated ? "Интегрировать и запустить" : "Запустить прокси";
    public string CurrentVersion { get => _currentVersion; private set => SetProperty(ref _currentVersion, value); }
    public string LatestVersion { get => _latestVersion; private set => SetProperty(ref _latestVersion, value); }
    public string ReleaseNotes { get => _releaseNotes; private set => SetProperty(ref _releaseNotes, value); }
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }
    public double UpdateProgress { get => _updateProgress; private set => SetProperty(ref _updateProgress, value); }
    public bool HasUpdate { get => _hasUpdate; private set { if (SetProperty(ref _hasUpdate, value)) { OnPropertyChanged(nameof(UpdateButtonText)); RefreshCommands(); } } }
    public string UpdateButtonText => HasUpdate ? "Установить обновление" : "Обновлений нет";
    public string TelegramProxyCurrentVersion
    {
        get => _telegramProxyCurrentVersion;
        private set
        {
            if (!SetProperty(ref _telegramProxyCurrentVersion, value)) return;
            OnPropertyChanged(nameof(TelegramProxyStatusText));
            OnPropertyChanged(nameof(TelegramProxyShortStatusText));
            OnPropertyChanged(nameof(TelegramProxyStartButtonText));
            OnPropertyChanged(nameof(TelegramProxyUpdateButtonText));
        }
    }
    public string TelegramProxyLatestVersion { get => _telegramProxyLatestVersion; private set => SetProperty(ref _telegramProxyLatestVersion, value); }
    public string TelegramProxyReleaseNotes { get => _telegramProxyReleaseNotes; private set => SetProperty(ref _telegramProxyReleaseNotes, value); }
    public string TelegramProxyUpdateStatus { get => _telegramProxyUpdateStatus; private set => SetProperty(ref _telegramProxyUpdateStatus, value); }
    public double TelegramProxyUpdateProgress { get => _telegramProxyUpdateProgress; private set => SetProperty(ref _telegramProxyUpdateProgress, value); }
    public bool HasTelegramProxyUpdate
    {
        get => _hasTelegramProxyUpdate;
        private set
        {
            if (!SetProperty(ref _hasTelegramProxyUpdate, value)) return;
            OnPropertyChanged(nameof(TelegramProxyUpdateButtonText));
            RefreshCommands();
        }
    }
    public string TelegramProxyUpdateButtonText => HasTelegramProxyUpdate ? _telegramProxy.IsInstalled ? "Установить обновление" : "Установить прокси" : "Обновлений нет";
    public string YouTubeStatus { get => _youTubeStatus; private set => SetProperty(ref _youTubeStatus, value); }
    public string DiscordStatus { get => _discordStatus; private set => SetProperty(ref _discordStatus, value); }
    public string ListContent { get => _listContent; set => SetProperty(ref _listContent, value); }

    public StrategyInfo? SelectedStrategy
    {
        get => _selectedStrategy;
        set { if (SetProperty(ref _selectedStrategy, value)) RefreshCommands(); }
    }

    public UserListDefinition? SelectedList
    {
        get => _selectedList;
        set
        {
            if (!SetProperty(ref _selectedList, value)) return;
            try
            {
                _strategies.EnsureUserLists();
                ListContent = value == null ? "" : File.ReadAllText(Path.Combine(_strategies.ListsPath, value.FileName));
            }
            catch (Exception ex) { ShowError("Не удалось открыть список", ex); }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            FilteredStrategies.Clear();
            foreach (var strategy in Strategies.Where(x => string.IsNullOrWhiteSpace(value) || x.Name.Contains(value, StringComparison.OrdinalIgnoreCase))) FilteredStrategies.Add(strategy);
        }
    }

    public async Task InitializeAsync()
    {
        RefreshRunningStatus();
        try
        {
            if (await _zapret.IsServiceInstalledAsync())
            {
                Settings.AutoStart = true;
                var installed = _zapret.GetServiceStrategy();
                var match = Strategies.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.FileName).Equals(installed, StringComparison.OrdinalIgnoreCase));
                if (match != null) SelectedStrategy = match;
            }
            Settings.IpSetMode = _strategies.GetIpSetMode();
            _log.Info($"Zapret Control запущен. Ядро {CurrentVersion}.");
            await AutoUpdateAsync();
        }
        catch (Exception ex) { _log.Error("Инициализация: " + ex.Message); }
    }

    private async Task AutoUpdateAsync()
    {
        if (!Settings.CheckForUpdates || IsBusy) return;
        if (!Settings.LastUpdateCheck.HasValue || (DateTime.UtcNow - Settings.LastUpdateCheck.Value).TotalHours >= 6)
        {
            await CheckUpdatesAsync(true);
            if (HasUpdate && Settings.AutoInstallUpdates && !IsBusy) await InstallUpdateAsync();
        }
        if (!_telegramProxy.IsInstalled || IsBusy) return;
        if (!Settings.LastTelegramProxyUpdateCheck.HasValue || (DateTime.UtcNow - Settings.LastTelegramProxyUpdateCheck.Value).TotalHours >= 6)
        {
            await CheckTelegramProxyUpdatesAsync(true);
            if (HasTelegramProxyUpdate && Settings.AutoInstallUpdates && !IsBusy) await InstallTelegramProxyUpdateAsync();
        }
    }

    private async Task StartStopAsync()
    {
        if (IsRunning) await RunBusy("Останавливаем обход…", async () => { await _zapret.StopAsync(); RefreshRunningStatus(); });
        else await StartSelectedAsync();
    }

    private async Task StartSelectedAsync()
    {
        if (SelectedStrategy == null) return;
        await RunBusy("Запускаем " + SelectedStrategy.Name + "…", async () =>
        {
            ValidateAndSaveSettings();
            await _zapret.StartAsync(SelectedStrategy, Settings);
            RefreshRunningStatus();
            _ = CheckConnectionsAsync();
        });
    }

    private async Task StartAllAsync() => await RunBusy("Запускаем обход и Telegram-прокси…", async () =>
    {
        if (SelectedStrategy == null) return;
        ValidateAndSaveSettings();
        if (!IsRunning) await _zapret.StartAsync(SelectedStrategy, Settings);
        await EnsureTelegramProxyInstalledAsync();
        if (!TelegramProxyIsRunning) await _telegramProxy.StartAsync(Settings);
        RefreshRunningStatus();
    });

    private async Task TelegramProxyStartStopAsync()
    {
        if (TelegramProxyIsRunning)
        {
            await RunBusy("Останавливаем Telegram-прокси…", async () => { await _telegramProxy.StopAsync(); RefreshRunningStatus(); });
            return;
        }
        await RunBusy("Запускаем Telegram-прокси…", async () =>
        {
            await EnsureTelegramProxyInstalledAsync();
            await _telegramProxy.StartAsync(Settings);
            RefreshRunningStatus();
        });
    }

    private async Task EnsureTelegramProxyInstalledAsync()
    {
        if (_telegramProxy.IsInstalled && _telegramProxy.IsIntegrated) return;
        var wasRunning = _telegramProxy.IsRunning;
        if (wasRunning) await _telegramProxy.StopAsync();
        TelegramProxyUpdateStatus = "Скачиваем интегрированный движок на основе релиза Flowseal…";
        TelegramProxyUpdateProgress = 0;
        var release = await _telegramProxyUpdates.CheckAsync();
        SetTelegramProxyRelease(release);
        var progress = new Progress<double>(value => TelegramProxyUpdateProgress = value);
        await _telegramProxyUpdates.InstallAsync(release, progress);
        TelegramProxyCurrentVersion = _telegramProxy.GetInstalledVersion();
        HasTelegramProxyUpdate = false;
        Settings.LastTelegramProxyUpdateCheck = DateTime.UtcNow;
        Settings.LatestTelegramProxyVersion = release.Version;
        _settingsService.Save(Settings);
        TelegramProxyUpdateStatus = $"Интегрированный Telegram-прокси {TelegramProxyCurrentVersion} установлен.";
        _log.Info(TelegramProxyUpdateStatus);
        if (wasRunning) await _telegramProxy.StartAsync(Settings);
    }

    private void OpenInTelegram()
    {
        try { OpenUrl(_telegramProxy.GetTelegramUrl()); }
        catch (Exception ex) { ShowError("Не удалось подключить Telegram", ex); }
    }

    private async Task RestartAsync() => await RunBusy("Перезапускаем обход…", async () =>
    {
        if (SelectedStrategy == null) return;
        ValidateAndSaveSettings();
        await _zapret.StartAsync(SelectedStrategy, Settings);
        RefreshRunningStatus();
    });

    private async Task ApplyStrategyAsync()
    {
        if (SelectedStrategy == null) return;
        Settings.SelectedStrategy = SelectedStrategy.FileName;
        CurrentPage = 0;
        await StartSelectedAsync();
    }

    private async Task SaveSettingsAsync() => await RunBusy("Применяем настройки…", async () =>
    {
        var shouldRun = IsRunning || Settings.AutoStart;
        var restartProxy = TelegramProxyIsRunning;
        ValidateAndSaveSettings();
        if (IsRunning) await _zapret.StopAsync();
        if (restartProxy) await _telegramProxy.StopAsync();
        if (!Settings.AutoStart) await _zapret.DeleteServiceAsync();
        _strategies.ApplySettings(Settings);
        if (shouldRun && SelectedStrategy != null) await _zapret.StartAsync(SelectedStrategy, Settings);
        if (restartProxy)
        {
            await EnsureTelegramProxyInstalledAsync();
            await _telegramProxy.StartAsync(Settings);
        }
        RefreshRunningStatus();
        OnPropertyChanged(nameof(TelegramProxyStatusDescription));
        _log.Info("Настройки сохранены.");
    });

    private void ValidateAndSaveSettings()
    {
        if (SelectedStrategy == null) throw new InvalidOperationException("Выберите стратегию.");
        StrategyService.ValidatePortRange(Settings.GameTcpPorts);
        StrategyService.ValidatePortRange(Settings.GameUdpPorts);
        if (Settings.TelegramProxyPort is < 1 or > 65535) throw new ArgumentException("Порт Telegram-прокси должен быть от 1 до 65535.");
        Settings.SelectedStrategy = SelectedStrategy.FileName;
        _settingsService.Save(Settings);
    }

    private async Task CheckUpdatesAsync(bool automatic)
    {
        await RunBusy("Проверяем релизы GitHub…", async () =>
        {
            UpdateStatus = "Запрашиваем последний релиз Flowseal…";
            UpdateProgress = 0;
            var release = await _updates.CheckAsync();
            _latestRelease = release;
            LatestVersion = release.Version;
            ReleaseNotes = string.IsNullOrWhiteSpace(release.Notes) ? "Автор не добавил описание релиза." : release.Notes;
            HasUpdate = UpdateService.IsNewer(release.Version, CurrentVersion);
            Settings.LastUpdateCheck = DateTime.UtcNow;
            Settings.LatestVersion = release.Version;
            _settingsService.Save(Settings);
            UpdateStatus = HasUpdate ? $"Доступна новая версия {release.Version}." : $"Установлена последняя версия {CurrentVersion}.";
            _log.Info(UpdateStatus);
        }, !automatic);
    }

    private async Task InstallUpdateAsync()
    {
        if (_latestRelease == null || !HasUpdate) return;
        await RunBusy("Обновляем ядро zapret…", async () =>
        {
            var wasRunning = IsRunning;
            var selectedFile = SelectedStrategy?.FileName ?? Settings.SelectedStrategy;
            UpdateStatus = "Скачиваем и проверяем официальный ZIP-релиз…";
            UpdateProgress = 0;
            if (wasRunning) await _zapret.StopAsync();
            var progress = new Progress<double>(value => UpdateProgress = value);
            await _updates.InstallAsync(_latestRelease, _strategies.EnginePath, progress);
            CurrentVersion = _strategies.GetCoreVersion();
            ReloadStrategies(selectedFile);
            _strategies.ApplySettings(Settings);
            HasUpdate = false;
            UpdateStatus = $"Ядро {CurrentVersion} установлено. Пользовательские списки сохранены.";
            _log.Info(UpdateStatus);
            if (wasRunning || Settings.AutoStart)
            {
                if (SelectedStrategy != null) await _zapret.StartAsync(SelectedStrategy, Settings);
                UpdateStatus += " Обход перезапущен.";
            }
            RefreshRunningStatus();
        }, true);
    }

    private async Task CheckTelegramProxyUpdatesAsync(bool automatic)
    {
        await RunBusy("Проверяем Telegram-прокси…", async () =>
        {
            TelegramProxyUpdateStatus = "Запрашиваем последний релиз tg-ws-proxy…";
            TelegramProxyUpdateProgress = 0;
            var release = await _telegramProxyUpdates.CheckAsync();
            SetTelegramProxyRelease(release);
            HasTelegramProxyUpdate = !_telegramProxy.IsInstalled || !_telegramProxy.IsIntegrated || UpdateService.IsNewer(release.Version, TelegramProxyCurrentVersion);
            Settings.LastTelegramProxyUpdateCheck = DateTime.UtcNow;
            Settings.LatestTelegramProxyVersion = release.Version;
            _settingsService.Save(Settings);
            TelegramProxyUpdateStatus = HasTelegramProxyUpdate ? $"Доступна версия {release.Version}." : $"Установлена последняя версия {TelegramProxyCurrentVersion}.";
            _log.Info("Telegram-прокси: " + TelegramProxyUpdateStatus);
        }, !automatic);
    }

    private void SetTelegramProxyRelease(ReleaseInfo release)
    {
        _latestTelegramProxyRelease = release;
        TelegramProxyLatestVersion = release.Version;
        TelegramProxyReleaseNotes = string.IsNullOrWhiteSpace(release.Notes) ? "Автор не добавил описание релиза." : release.Notes;
    }

    private async Task InstallTelegramProxyUpdateAsync()
    {
        if (_latestTelegramProxyRelease == null || !HasTelegramProxyUpdate) return;
        await RunBusy("Обновляем Telegram-прокси…", async () =>
        {
            var wasRunning = TelegramProxyIsRunning;
            TelegramProxyUpdateStatus = "Скачиваем и проверяем интегрированный EXE-движок…";
            TelegramProxyUpdateProgress = 0;
            if (wasRunning) await _telegramProxy.StopAsync();
            var progress = new Progress<double>(value => TelegramProxyUpdateProgress = value);
            await _telegramProxyUpdates.InstallAsync(_latestTelegramProxyRelease, progress);
            TelegramProxyCurrentVersion = _telegramProxy.GetInstalledVersion();
            HasTelegramProxyUpdate = false;
            TelegramProxyUpdateStatus = $"Telegram-прокси {TelegramProxyCurrentVersion} установлен. Настройки и секрет сохранены.";
            _log.Info(TelegramProxyUpdateStatus);
            if (wasRunning)
            {
                await _telegramProxy.StartAsync(Settings);
                TelegramProxyUpdateStatus += " Прокси перезапущен.";
            }
            RefreshRunningStatus();
        }, true);
    }

    private void ReloadStrategies(string selectedFile)
    {
        Strategies.Clear();
        FilteredStrategies.Clear();
        foreach (var strategy in _strategies.LoadStrategies()) { Strategies.Add(strategy); FilteredStrategies.Add(strategy); }
        SelectedStrategy = Strategies.FirstOrDefault(x => x.FileName == selectedFile) ?? Strategies.FirstOrDefault();
        if (SelectedStrategy != null) Settings.SelectedStrategy = SelectedStrategy.FileName;
        _settingsService.Save(Settings);
    }

    private async Task RunDiagnosticsAsync() => await RunBusy("Выполняем диагностику…", async () =>
    {
        Diagnostics.Clear();
        foreach (var item in await _diagnostics.RunAsync()) Diagnostics.Add(item);
        _log.Info("Диагностика завершена.");
    });

    private async Task CheckConnectionsAsync() => await RunBusy("Проверяем доступность сервисов…", async () =>
    {
        YouTubeStatus = DiscordStatus = "Проверка…";
        var youtube = DiagnosticsService.CheckConnectionAsync("YouTube");
        var discord = DiagnosticsService.CheckConnectionAsync("Discord");
        await Task.WhenAll(youtube, discord);
        YouTubeStatus = youtube.Result.Text;
        DiscordStatus = discord.Result.Text;
    }, true);

    private async Task SaveListAsync() => await RunBusy("Сохраняем список…", async () =>
    {
        if (SelectedList == null) return;
        await File.WriteAllTextAsync(Path.Combine(_strategies.ListsPath, SelectedList.FileName), ListContent);
        _log.Info($"Сохранён {SelectedList.FileName}. Перезапустите обход для применения.");
    });

    private async Task UpdateIpSetAsync() => await RunBusy("Обновляем IPSet…", async () =>
    {
        await _strategies.UpdateIpSetAsync();
        Settings.IpSetMode = "loaded";
        _settingsService.Save(Settings);
        OnPropertyChanged(nameof(Settings));
        _log.Info("IPSet обновлён из репозитория. Перезапустите обход для применения.");
    }, true);

    private void RefreshRunningStatus()
    {
        IsRunning = _zapret.IsRunning;
        TelegramProxyIsRunning = _telegramProxy.IsRunning;
    }

    private async Task RunBusy(string text, Func<Task> action, bool showErrors = true)
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyText = text;
        try { await action(); }
        catch (Exception ex)
        {
            _log.Error(text + " " + ex.Message);
            if (showErrors) ShowError("Операция не выполнена", ex);
        }
        finally { IsBusy = false; BusyText = ""; RefreshRunningStatus(); }
    }

    private void RefreshCommands()
    {
        if (StartStopCommand is null) return;
        StartStopCommand.RaiseCanExecuteChanged();
        StartAllCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
        ApplyStrategyCommand.RaiseCanExecuteChanged();
        TelegramProxyStartStopCommand.RaiseCanExecuteChanged();
        OpenInTelegramCommand.RaiseCanExecuteChanged();
        CheckUpdatesCommand.RaiseCanExecuteChanged();
        InstallUpdateCommand.RaiseCanExecuteChanged();
        CheckTelegramProxyUpdatesCommand.RaiseCanExecuteChanged();
        InstallTelegramProxyUpdateCommand.RaiseCanExecuteChanged();
        RunDiagnosticsCommand.RaiseCanExecuteChanged();
        SaveSettingsCommand.RaiseCanExecuteChanged();
        SaveListCommand.RaiseCanExecuteChanged();
        UpdateIpSetCommand.RaiseCanExecuteChanged();
        CheckConnectionsCommand.RaiseCanExecuteChanged();
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private static void ShowError(string title, Exception ex) => System.Windows.MessageBox.Show(ex.Message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
}
