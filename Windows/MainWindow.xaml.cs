using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZapretControl.ViewModels;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ZapretControl;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _trayToggleAll;
    private Forms.ToolStripMenuItem? _trayToggleZapret;
    private Forms.ToolStripMenuItem? _trayToggleProxy;
    private Forms.ToolStripMenuItem? _trayConnect;
    private bool _allowClose;
    private bool _trayHintShown;

    public MainWindow(bool initialize = true, bool enableTray = true)
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.RestartRequested += () =>
        {
            _allowClose = true;
            System.Windows.Application.Current.Shutdown();
        };
        if (enableTray) InitializeTray();
        if (initialize) Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    internal async Task InitializeAsync() => await _viewModel.InitializeAsync();

    private void InitializeTray()
    {
        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add("Открыть Zapret Control", null, (_, _) => RestoreFromTray());
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        _trayToggleAll = new Forms.ToolStripMenuItem();
        _trayToggleAll.Click += async (_, _) => await _viewModel.ToggleAllAsync();
        _trayMenu.Items.Add(_trayToggleAll);
        _trayToggleZapret = new Forms.ToolStripMenuItem();
        _trayToggleZapret.Click += (_, _) => _viewModel.StartStopCommand.Execute(null);
        _trayMenu.Items.Add(_trayToggleZapret);
        _trayToggleProxy = new Forms.ToolStripMenuItem();
        _trayToggleProxy.Click += (_, _) => _viewModel.TelegramProxyStartStopCommand.Execute(null);
        _trayMenu.Items.Add(_trayToggleProxy);
        _trayConnect = new Forms.ToolStripMenuItem("Подключить Telegram");
        _trayConnect.Click += (_, _) => _viewModel.OpenInTelegramCommand.Execute(null);
        _trayMenu.Items.Add(_trayConnect);

        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Закрыть полностью", null, (_, _) => ExitFromTray());
        _trayMenu.Opening += (_, _) => UpdateTrayMenu();
        var processPath = Environment.ProcessPath;
        var icon = processPath == null ? Drawing.SystemIcons.Application : Drawing.Icon.ExtractAssociatedIcon(processPath) ?? Drawing.SystemIcons.Application;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Zapret Control — обход и Telegram-прокси",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        UpdateTrayMenu();
    }

    private void UpdateTrayMenu()
    {
        if (_trayToggleAll == null || _trayToggleZapret == null || _trayToggleProxy == null || _trayConnect == null) return;
        _trayToggleAll.Text = _viewModel.IsRunning || _viewModel.TelegramProxyIsRunning ? "Остановить всё" : "Запустить всё";
        _trayToggleZapret.Text = _viewModel.IsRunning ? "Остановить обход" : "Запустить обход";
        _trayToggleProxy.Text = _viewModel.TelegramProxyIsRunning ? "Остановить Telegram-прокси" : "Запустить Telegram-прокси";
        _trayToggleAll.Enabled = _trayToggleZapret.Enabled = _trayToggleProxy.Enabled = !_viewModel.IsBusy;
        _trayConnect.Enabled = !_viewModel.IsBusy && _viewModel.TelegramProxyIsRunning;
    }

    internal void RestoreFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void HideToTray()
    {
        if (_trayIcon == null) return;
        Hide();
        if (_trayHintShown) return;
        _trayHintShown = true;
        _trayIcon.ShowBalloonTip(4000, "Zapret Control продолжает работать", "Обход и Telegram-прокси активны. Для полного выхода нажмите правой кнопкой по значку в трее и выберите «Закрыть полностью».", Forms.ToolTipIcon.Info);
    }

    internal void ExitFromTray()
    {
        _allowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string tag } && int.TryParse(tag, out var page)) _viewModel.CurrentPage = page;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            System.Windows.MessageBox.Show("Дождитесь завершения текущей операции.", "Zapret Control", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            e.Cancel = true;
            return;
        }
        if (!_allowClose && _trayIcon != null)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayMenu?.Dispose();
        base.OnClosed(e);
    }
}
