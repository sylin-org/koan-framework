using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Cache.CrossEngine.Specs;

/// <summary>
/// Runs <see cref="CrossEngineCacheBehaviorSpecBase"/> against the default Memory store.
/// No adapter wiring needed — <c>AddKoanCache()</c> registers <c>MemoryCacheStore</c> as
/// the default L1.
/// </summary>
public sealed class MemoryEngineSpec : CrossEngineCacheBehaviorSpecBase
{
    protected override string EngineName => "Memory";

    protected override void ConfigureAdapter(IServiceCollection services)
    {
        // Intentionally empty — AddKoanCache (called in the base) already registers
        // MemoryCacheStore. This subclass exists to assert the universal abstraction works
        // with the framework's default engine.
    }
}
