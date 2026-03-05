namespace Koan.Core.AI;

/// <summary>
/// Well-known AI capability identifiers used by <see cref="IAiModelAdvisor"/>
/// and the AI category router. Use these instead of raw strings when calling
/// <c>IAiModelAdvisor.GetRecommendedModel()</c> or <c>ZenGarden.RecommendedModel()</c>.
/// </summary>
public static class AiCapability
{
    public const string Chat = "Chat";
    public const string Embed = "Embed";
    public const string Ocr = "Ocr";
    public const string Vision = "Vision";
    public const string Quick = "Quick";
    public const string Synthesis = "Synthesis";
    public const string Thinking = "Thinking";
    public const string Tools = "Tools";
}
