namespace Sora.Data.Vector.Abstractions;

/// <summary>
/// Instruction name constants for vector adapters.
/// </summary>
public static class VectorInstructions
{
    public const string IndexEnsureCreated = "vector.index.ensureCreated";
    public const string IndexRebuild = "vector.index.rebuild";
    public const string IndexClear = "vector.index.clear";
    public const string IndexStats = "vector.index.stats";
}
