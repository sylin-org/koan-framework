namespace Koan.Core.AI;

/// <summary>
/// Provides model bindings from a named recipe configuration.
/// Recipes are curated capability-to-model mappings authored by ML engineers
/// or DevOps specialists, sitting between developer overrides and automated
/// orchestrator recommendations in the resolution chain.
///
/// Resolution priority in the AI pipeline:
///   1. Explicit <c>ChatOptions.Model</c> on the call
///   2. Ambient <c>AiCategoryScope</c> model override
///   3. <b>Active recipe binding</b> (this interface)
///   4. <c>IAiModelAdvisor</c> recommendation (orchestrator)
///   5. Category configuration (<c>Koan:Ai:{Category}:Model</c>)
///   6. Source/member default model
///   7. Hardcoded fallback
///
/// Return null from <see cref="GetModel"/> to defer to the next resolution step.
/// A recipe is sparse — it need not bind every capability.
/// </summary>
/// <seealso cref="IAiModelAdvisor"/>
public interface IAiRecipeProvider
{
    /// <summary>
    /// Name of the currently active recipe, or null if no recipe is active.
    /// </summary>
    string? ActiveRecipeName { get; }

    /// <summary>
    /// Returns the model name bound to the given category in the active recipe,
    /// or null if the recipe has no binding for this category.
    /// </summary>
    /// <param name="category">AI category from <see cref="AiCapability"/> constants.</param>
    string? GetModel(string category);
}
