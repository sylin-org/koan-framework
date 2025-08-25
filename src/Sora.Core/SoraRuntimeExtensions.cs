using Microsoft.Extensions.DependencyInjection;

namespace Sora.Core;

// Runtime hooks for discovery/start stages

public static class SoraRuntimeExtensions
{
    public static IServiceProvider UseSora(this IServiceProvider sp)
    {
    // Ensure ambient provider is available for terse APIs; always refresh to current provider
    // This avoids leaking a disposed provider across WebApplicationFactory instances in tests.
    SoraApp.Current = sp;
        // Initialize SoraEnv once we have DI
        try { SoraEnv.TryInitialize(sp); } catch { }
        var rt = sp.GetService<ISoraRuntime>();
        rt?.Discover();
        return sp;
    }

    public static IServiceProvider StartSora(this IServiceProvider sp)
    {
        // Ensure SoraEnv initialized even if UseSora wasn’t called
        try { SoraEnv.TryInitialize(sp); } catch { }
        var rt = sp.GetService<ISoraRuntime>();
        rt?.Start();
        return sp;
    }
}

// Minimal ambient accessor (opt-in) to support terse extension methods