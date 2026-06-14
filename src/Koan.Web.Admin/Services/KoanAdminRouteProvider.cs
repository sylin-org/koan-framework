using Koan.Web.Admin.Contracts;
using Koan.Web.Admin.Infrastructure;
using Koan.Web.Admin.Options;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Services;

internal sealed class KoanAdminRouteProvider : IKoanAdminRouteProvider, IDisposable
{
    private readonly IDisposable? _subscription;
    private KoanAdminRouteMap _current;

    public KoanAdminRouteProvider(IOptionsMonitor<KoanAdminOptions> options)
    {
        _current = CreateMap(options.CurrentValue);
        _subscription = options.OnChange(o => _current = CreateMap(o));
    }

    public KoanAdminRouteMap Current => _current;

    internal static KoanAdminRouteMap CreateMap(KoanAdminOptions options)
    {
        var prefix = KoanAdminPathUtility.NormalizePrefix(options.PathPrefix);
        var root = KoanAdminPathUtility.BuildTemplate(prefix, "admin");
        var api = KoanAdminPathUtility.BuildTemplate(root, "api");
        return new KoanAdminRouteMap(prefix, root, api);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
