using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Immutable recipe — an ordered set of <see cref="MediaStep"/>s plus
/// the mutator allowlist callers may apply via URL or builder
/// overrides. Per MEDIA-0004 §2.
///
/// Recipes are content-hashable via <see cref="Fingerprint"/>; the
/// hash is the recipe-side half of every cache key:
/// <c>(sourceHash, recipeFingerprint)</c>.
///
/// Construct via <see cref="New"/> (fluent builder) or directly when
/// the step list is already known.
/// </summary>
public sealed record MediaRecipe
{
    /// <summary>Registered name; null for anonymous (ad-hoc URL) recipes.</summary>
    public string? Name { get; init; }

    /// <summary>Human-readable description; surfaces in <c>/media/recipes</c>.</summary>
    public string? Description { get; init; }

    /// <summary>Recipe schema version. Bump when step grammar changes.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Ordered steps. Stage order is enforced at materialise time, not at construction.</summary>
    public ImmutableArray<MediaStep> Steps { get; init; } = ImmutableArray<MediaStep>.Empty;

    /// <summary>URL/builder overrides this recipe accepts. Default <see cref="MutatorKind.None"/>.</summary>
    public MutatorKind AllowedMutators { get; init; } = MutatorKind.None;

    /// <summary>
    /// When true, this recipe pre-warms at upload time via
    /// <c>POST /api/admin/media/{id}/warm</c>. Default false (lazy).
    /// </summary>
    public bool Eager { get; init; }

    /// <summary>
    /// Origin of the recipe — used by introspection JSON to show
    /// whether ops has overridden a code-defined recipe via appsettings.
    /// </summary>
    public RecipeSource Source { get; init; } = RecipeSource.Code;

    /// <summary>Start a new fluent builder.</summary>
    public static MediaRecipeBuilder New() => new();

    /// <summary>
    /// Stable SHA-256 fingerprint over the canonicalised step list.
    /// Used as the recipe-side cache key half. Returns the first 16
    /// hex characters (8 bytes / 64 bits) — collision-resistant enough
    /// for cache keys at expected scale, short enough for ETag values.
    /// </summary>
    public string Fingerprint()
    {
        var sb = new StringBuilder(256);
        sb.Append("v=").Append(Version).Append('|');
        // Sort by stage then declaration index for stable hashing
        var ordered = Steps
            .Select((s, i) => (s, i))
            .OrderBy(t => (int)t.s.Stage)
            .ThenBy(t => t.i);
        foreach (var (step, _) in ordered)
        {
            if (step.Name is { Length: > 0 } n) sb.Append('[').Append(n).Append(']');
            step.WriteFingerprint(sb);
            sb.Append(';');
        }
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()), hash);
        return Convert.ToHexStringLower(hash[..8]);
    }

    /// <summary>
    /// Returns the step marked <c>Primary</c> of the given kind, or
    /// the single step of that kind if exactly one exists. Returns
    /// null when zero or more-than-one-non-primary steps exist of
    /// that kind. Used by URL override resolution for unprefixed
    /// mutators like <c>?w=</c>.
    /// </summary>
    public T? FindPrimary<T>() where T : MediaStep
    {
        T? sole = null;
        var seenMultiple = false;
        foreach (var step in Steps)
        {
            if (step is not T typed) continue;
            if (step.Primary) return typed;
            if (sole is null) sole = typed;
            else seenMultiple = true;
        }
        return seenMultiple ? null : sole;
    }
}

public enum RecipeSource
{
    /// <summary>Discovered via <c>[MediaRecipe]</c> attribute scan.</summary>
    Code = 0,

    /// <summary>Bound from <c>Koan:Media:Recipes</c> config section.</summary>
    Config = 1,

    /// <summary>Config-defined recipe with the same name as a code recipe — config wins.</summary>
    ConfigOverride = 2,

    /// <summary>Built dynamically from URL query (no registry entry).</summary>
    AdHoc = 3,
}
