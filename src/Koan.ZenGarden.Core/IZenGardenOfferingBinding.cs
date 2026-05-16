namespace Koan.ZenGarden.Core;

/// <summary>
/// Adapter-provided metadata that maps an adapter id to its default Zen Garden offering name.
/// </summary>
public interface IZenGardenOfferingBinding
{
    /// <summary>
    /// Adapter identifier (for example: "mongo", "mongodb", "ollama").
    /// </summary>
    string AdapterId { get; }

    /// <summary>
    /// Default offering selector used when adapters request Zen Garden resolution in auto mode.
    /// </summary>
    string Offering { get; }
}
