using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace ZapretControl.Services;

public sealed class LogService
{
    private readonly string _logFile;

    public LogService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretControl", "logs");
        Directory.CreateDirectory(directory);
        _logFile = Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public ObservableCollection<string> Entries { get; } = new();

    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        try { File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}"); } catch { }
        var add = () => Entries.Add(line);
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true) add();
        else System.Windows.Application.Current?.Dispatcher.BeginInvoke(add);
    }
}
