namespace ZapretControl.Models;

public sealed class ReleaseInfo
{
    public string Version { get; init; } = "";
    public string Notes { get; init; } = "";
    public string PageUrl { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
    public DateTime PublishedAt { get; init; }
}
