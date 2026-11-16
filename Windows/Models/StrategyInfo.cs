namespace ZapretControl.Models;

public sealed class StrategyInfo
{
    public string Name { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsRecommended => FileName.Equals("general.bat", StringComparison.OrdinalIgnoreCase);
}
