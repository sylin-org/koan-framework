namespace Sora.Core.Hosting.App;

// Ambient service provider holder for terse APIs and cross-cutting helpers.
public static class AppHost
{
    private static IServiceProvider? _current;
    public static IServiceProvider? Current
    {
        get => _current;
        set => _current = value;
    }
}
