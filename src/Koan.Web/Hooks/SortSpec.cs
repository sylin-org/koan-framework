namespace Koan.Web.Hooks;

/// <summary>
/// Field-based sort specification.
/// </summary>
public sealed record SortSpec(string Field, bool Desc);