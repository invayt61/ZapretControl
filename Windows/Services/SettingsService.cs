using System.IO;
using System.Text.Json;
using ZapretControl.Models;

namespace ZapretControl.Services;

public sealed class SettingsService
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZapretControl");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _filePath, true);
    }
}
