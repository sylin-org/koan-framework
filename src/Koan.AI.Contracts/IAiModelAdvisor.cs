namespace Koan.AI.Contracts;

/// <summary>
/// Service Provider Interface for dynamic AI model recommendations.
/// Implementations provide the best available model for a given AI category
/// based on runtime conditions (e.g., orchestrator recommendations, garden availability).
///
/// Resolution priority in the AI pipeline:
///   1. Explicit <c>ChatOptions.Model</c> on the call
///   2. Ambient <c>AiCategoryScope</c> model override
///   3. Active recipe binding (<see cref="IAiRecipeProvider"/>)
///   4. <b>IAiModelAdvisor recommendation</b> (this interface)
///   5. Category configuration (<c>Koan:Ai:{Category}:Model</c>)
///   6. Source/member default model
///   7. Hardcoded fallback
///
/// Categories are defined in <see cref="AiCapability"/>: Chat, Embed, Ocr, Vision, etc.
/// Return null to defer to the next resolution step.
/// </summary>
public interface IAiModelAdvisor
{
    /// <summary>
    /// Returns the recommended model name for the given AI category,
    /// or null if no recommendation is available.
    /// </summary>
    /// <param name="category">AI category from <see cref="AiCapability"/> constants.</param>
    string? GetRecommendedModel(string category);
}
