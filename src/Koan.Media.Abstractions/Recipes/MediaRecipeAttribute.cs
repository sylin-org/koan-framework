namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Marks a <c>static MediaRecipe</c>-returning method as a registered
/// recipe. Discovered via Koan's assembly-scan registrar; lands in
/// <see cref="IMediaRecipeRegistry"/> at boot.
///
/// Example:
/// <code>
/// public static class MediaRecipes
/// {
///     [MediaRecipe("poster",
///         Description = "Single still frame, fits 800x800 square, WebP q80",
///         Mutators = MutatorKind.Common | MutatorKind.Frame)]
///     public static MediaRecipe Poster() =&gt; MediaRecipe.New()
///         .ExtractFrame(0)
///         .Crop("1:1")
///         .Fit(Fit.Cover)
///         .Resize(width: 800).Name("size").Primary()
///         .EncodeAs("webp", Quality.Web);
/// }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class MediaRecipeAttribute(string name) : Attribute
{
    /// <summary>Slug used in URLs (<c>/media/{id}/{name}</c>) and the registry key.</summary>
    public string Name { get; } = name;

    /// <summary>Human-readable description; surfaces in <c>/media/recipes</c>.</summary>
    public string? Description { get; init; }

    /// <summary>Recipe schema version. Bump when step grammar changes.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Which URL/builder override classes are accepted.</summary>
    public MutatorKind Mutators { get; init; } = MutatorKind.None;

    /// <summary>Pre-warm at upload time (vs lazy-on-first-request).</summary>
    public bool Eager { get; init; }
}
