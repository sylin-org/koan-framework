namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Registry of named <see cref="MediaRecipe"/>s, populated from both
/// <c>[MediaRecipe]</c> attribute scanning and the
/// <c>Koan:Media:Recipes</c> configuration section. Per MEDIA-0004 §3.
///
/// Resolution order during URL handling:
/// <list type="number">
///   <item>Named recipe → returns the recipe</item>
///   <item>Format shortcut (<c>png</c>, <c>jpeg</c>, <c>webp</c>, <c>gif</c>, <c>avif</c>) → synthesises an EncodeAs recipe</item>
///   <item>Unrecognised seed → 404</item>
/// </list>
/// </summary>
public interface IMediaRecipeRegistry
{
    /// <summary>All registered recipes (excluding format shortcuts).</summary>
    IReadOnlyList<MediaRecipe> All { get; }

    /// <summary>Reserved format shortcut names that the controller resolves to synthetic encode recipes.</summary>
    IReadOnlyList<string> FormatShortcuts { get; }

    /// <summary>
    /// Resolve a seed name to a recipe. Returns true and a
    /// <see cref="MediaRecipe"/> when the seed names a registered
    /// recipe OR a format shortcut. Returns false for unknown seeds.
    /// </summary>
    bool TryResolve(string seed, out MediaRecipe recipe);

    /// <summary>Lookup by exact registry name (ignores format shortcuts).</summary>
    MediaRecipe? Find(string name);
}
