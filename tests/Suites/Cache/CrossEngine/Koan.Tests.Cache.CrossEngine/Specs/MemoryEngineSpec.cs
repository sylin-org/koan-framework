namespace Koan.Tests.Cache.CrossEngine.Specs;

/// <summary>
/// Runs <see cref="CrossEngineCacheBehaviorSpecBase"/> against the Memory store.
/// </summary>
/// <remarks>
/// No extra settings — the Memory store is bundled with <c>Koan.Cache</c> and registers
/// itself as the Cache pillar's built-in provider. <c>Koan:Cache:LocalProvider=memory</c> tells the
/// topology resolver to pick it even though the higher-priority SQLite adapter is also
/// referenced by this project (Reference = Intent: both are discovered; configuration
/// chooses).
/// </remarks>
public sealed class MemoryEngineSpec : CrossEngineCacheBehaviorSpecBase
{
    protected override string LocalProvider => "memory";
}
