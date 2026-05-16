namespace Koan.Core.Hosting.App;

// Ambient service provider holder for terse APIs and cross-cutting helpers.
public static class AppHost
{
    private static IServiceProvider? _current;
    private static ApplicationIdentitySnapshot _identity = ApplicationIdentitySnapshot.Empty;

    public static IServiceProvider? Current
    {
        get => _current;
        set => _current = value;
    }

    public static ApplicationIdentitySnapshot Identity => _identity;

    internal static void SetIdentity(ApplicationIdentitySnapshot snapshot)
    {
        _identity = snapshot;
    }
}
