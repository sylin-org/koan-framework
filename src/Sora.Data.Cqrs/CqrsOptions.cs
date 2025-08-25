namespace Sora.Data.Cqrs;

/// <summary>
/// CQRS configuration bound from Sora:Cqrs.
/// </summary>
public sealed class CqrsOptions
{
    public string? DefaultProfile { get; set; }
    public Dictionary<string, CqrsProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}