using Koan.Admin.Contracts;
using Koan.Admin.Options;
using Microsoft.Extensions.Options;

namespace Koan.Admin.Services;

internal sealed class KoanAdminFeatureManager : IKoanAdminFeatureManager, IDisposable
{
    private readonly IDisposable? _subscription;
    private KoanAdminFeatureSnapshot _current;

    public KoanAdminFeatureManager(IKoanAdminRouteProvider routes, IOptionsMonitor<KoanAdminOptions> options)
    {
        _current = Build(options.CurrentValue, routes.Current);
        _subscription = options.OnChange(o => _current = Build(o, KoanAdminRouteProvider.CreateMap(o)));
    }

    public KoanAdminFeatureSnapshot Current => _current;

    private static KoanAdminFeatureSnapshot Build(KoanAdminOptions options, KoanAdminRouteMap routes)
    {
        var environmentAllowed = Koan.Core.KoanEnv.IsDevelopment
            || (!Koan.Core.KoanEnv.IsProduction && !Koan.Core.KoanEnv.IsStaging)
            || options.AllowInProduction;

        var enabled = options.Enabled && environmentAllowed;
        var webEnabled = enabled && options.EnableWeb;
        var consoleEnabled = enabled && options.EnableConsoleUi;
        var manifestExposed = enabled && options.ExposeManifest;
        var destructive = enabled && options.DestructiveOps;
        var allowLog = enabled && options.Logging.AllowTranscriptDownload;

        var dotPrefixAllowed = !routes.Prefix.StartsWith('.', StringComparison.Ordinal)
            || Koan.Core.KoanEnv.IsDevelopment
            || options.AllowDotPrefixInProduction;

        return new KoanAdminFeatureSnapshot(
            enabled,
            webEnabled,
            consoleEnabled,
            manifestExposed,
            destructive,
            allowLog,
            routes,
            routes.Prefix,
            dotPrefixAllowed
        );
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
