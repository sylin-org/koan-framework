using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;

namespace Koan.Media.Core.Recipes;

/// <summary>
/// Boot-time guard that a recipe only declares output formats the framework can actually produce.
/// Per DATA-0098 / MEDIA-0009: producibility is owned solely by <see cref="EncoderSelector"/>. A recipe
/// pinning a non-producible format — "avif" before its encoder is wired, a typo, or an unknown slug —
/// would otherwise bind clean and 500 at request time (the planner's terminal gate is intentionally
/// permissive for unknown slugs, deferring to <see cref="EncoderSelector.For"/> which throws). Both the
/// config path (<see cref="ConfiguredRecipeBinder"/>) and the code path
/// (<c>MediaRecipeRegistry.DiscoverCodeRecipes</c>) run every built recipe through this, so a misconfigured
/// format fails fast at startup with a clear message instead of per-request.
/// </summary>
internal static class RecipeOutputFormatValidator
{
    public static void EnsureProducible(MediaRecipe recipe, string name)
    {
        foreach (var step in recipe.Steps)
        {
            // null EncodeStep.Format means "preserve source format" — nothing to validate.
            var format = step switch
            {
                EncodeStep e => e.Format,
                FlattenToStep f => f.Format,
                _ => null,
            };
            if (format is null) continue;

            if (!EncoderSelector.CanProduce(format))
            {
                throw new MediaRecipeBindingException(
                    $"Recipe '{name}': output format '{format}' is not producible. " +
                    $"Producible formats: {string.Join(", ", EncoderSelector.SupportedFormats)} (alias: jpg→jpeg). " +
                    "A format must have a concrete encoder in EncoderSelector before a recipe may target it.");
            }
        }
    }
}
