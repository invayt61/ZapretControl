namespace ZapretControl.Models;

public sealed class DiagnosticItem
{
    public string Name { get; init; } = "";
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
}
