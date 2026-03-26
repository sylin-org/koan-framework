using Koan.AI.Infrastructure;
using Koan.Core.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Pipeline;

/// <summary>
/// Reads recipe bindings from <c>Koan:Ai:Recipes:{name}</c> configuration sections.
/// The active recipe is selected by <c>Koan:Ai:ActiveRecipe</c>.
///
/// Recipes are sparse capability-to-model maps authored by ML engineers or DevOps.
/// A missing key means "no opinion" — the next provider in the resolution chain decides.
/// </summary>
internal sealed class AiRecipeProvider : IAiRecipeProvider
{
    private readonly IReadOnlyDictionary<string, string> _bindings;
    private readonly ILogger<AiRecipeProvider>? _logger;

    public string? ActiveRecipeName { get; }

    public AiRecipeProvider(IConfiguration configuration, ILogger<AiRecipeProvider>? logger = null)
    {
        _logger = logger;

        var recipeName = configuration[ConfigurationConstants.Recipe.ActiveRecipe];
        if (string.IsNullOrWhiteSpace(recipeName))
        {
            ActiveRecipeName = null;
            _bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        ActiveRecipeName = recipeName.Trim();
        var section = configuration.GetSection(ConfigurationConstants.Recipe.ForRecipe(ActiveRecipeName));

        if (!section.Exists())
        {
            _logger?.LogWarning(
                "Active AI recipe '{RecipeName}' not found in configuration — no recipe bindings active",
                ActiveRecipeName);
            _bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                bindings[child.Key] = child.Value.Trim();
            }
        }

        _bindings = bindings;

        if (bindings.Count > 0)
        {
            _logger?.LogInformation(
                "AI recipe '{RecipeName}' active with {Count} bindings: {Bindings}",
                ActiveRecipeName,
                bindings.Count,
                string.Join(", ", bindings.Select(kv => $"{kv.Key}={kv.Value}")));
        }
    }

    public string? GetModel(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return null;

        if (_bindings.TryGetValue(category, out var model))
        {
            _logger?.LogDebug(
                "Recipe '{RecipeName}' resolved {Category} → {Model}",
                ActiveRecipeName, category, model);
            return model;
        }

        return null;
    }
}
