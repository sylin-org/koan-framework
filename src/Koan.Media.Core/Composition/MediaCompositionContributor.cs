using Koan.Core.Composition;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Recipes;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Media.Core.Composition;

/// <summary>Projects the materialized Media recipe catalog into shared runtime facts.</summary>
internal sealed class MediaCompositionContributor : IKoanCompositionContributor
{
    private const string RecipesSubject = "media:recipes";
    private const string RecipeSubjectPrefix = "media:recipe:";
    private const string RecipesDiscoveredCode = "koan.media.recipes.discovered";
    private const string RecipeDiscoveredCode = "koan.media.recipe.discovered";
    private const string TypedDiscoveryReason = "typed-recipe-discovery";

    public void Contribute(KoanCompositionBuilder builder, IServiceProvider services)
    {
        var registry = services.GetService<IMediaRecipeRegistry>();
        if (registry is null) return;

        var recipes = registry.All;
        var shortcuts = registry.FormatShortcuts;
        builder.AddConfigKey(RecipesOptions.SectionPath);
        builder.AddCapability(
            RecipesSubject,
            ["named-recipes", "configuration-overrides", "format-shortcuts", "fingerprinted-derivations"]);
        builder.AddObservation(
            RecipesDiscoveredCode,
            RecipesSubject,
            $"Koan materialized {recipes.Count} named Media recipe(s) and {shortcuts.Count} producible format shortcut(s): " +
            (shortcuts.Count == 0 ? "none." : $"{string.Join(", ", shortcuts)}."),
            TypedDiscoveryReason,
            typeof(MediaCompositionContributor).FullName);

        foreach (var recipe in recipes)
        {
            var name = recipe.Name ?? "anonymous";
            var formats = recipe.AllowedOutputFormats.IsDefaultOrEmpty
                ? "preserve-source"
                : string.Join(",", recipe.AllowedOutputFormats);
            builder.AddObservation(
                RecipeDiscoveredCode,
                RecipeSubjectPrefix + name,
                $"Media recipe '{name}' is {recipe.Source}, version {recipe.Version}, fingerprint {recipe.Fingerprint()}, " +
                $"with {recipe.Steps.Length} step(s), mutators={recipe.AllowedMutators}, formats={formats}.",
                TypedDiscoveryReason,
                typeof(MediaCompositionContributor).FullName);
        }
    }
}
