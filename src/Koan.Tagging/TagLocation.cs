namespace Koan.Tagging;

/// <summary>
/// Where a tag was found in a <see cref="TagSet"/>. Returned by <see cref="TagSet.Find(string)"/>
/// so callers can answer "do you have ffxiv? — yes, it's in Public/game" style inspections.
/// </summary>
public sealed record TagLocation(TagSet.EScope Scope, string Category);
