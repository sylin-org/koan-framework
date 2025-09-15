namespace Koan.Data.Core.Options;

public sealed class DirectOptions
{
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRows { get; set; } = 10_000;
}
