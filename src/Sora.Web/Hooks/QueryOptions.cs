namespace Sora.Web.Hooks;

/// <summary>
/// Query and shaping options flowing through the controller and hooks.
/// </summary>
public sealed class QueryOptions
{
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = Infrastructure.SoraWebConstants.Defaults.DefaultPageSize;
    public List<SortSpec> Sort { get; set; } = new();
    public string Shape { get; set; } = "full"; // full | map | dict
    public string? View { get; set; }
    public Dictionary<string, string> Extras { get; } = new();
}