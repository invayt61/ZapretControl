namespace ZapretControl.Models;

public sealed class AppSettings
{
    public string SelectedStrategy { get; set; } = "general.bat";
    public string GameFilter { get; set; } = "all";
    public string GameTcpPorts { get; set; } = "1024-65535";
    public string GameUdpPorts { get; set; } = "1024-65535";
    public string IpSetMode { get; set; } = "loaded";
    public bool AutoStart { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool AutoInstallUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public string? LatestVersion { get; set; }
    public DateTime? LastTelegramProxyUpdateCheck { get; set; }
    public string? LatestTelegramProxyVersion { get; set; }
}
