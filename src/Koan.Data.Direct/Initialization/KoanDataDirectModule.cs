using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Core.Direct;

namespace Koan.Data.Direct.Initialization;

/// <summary>
/// Koan.Data.Direct boot module (ARCH-0086). Folds the former <c>KoanDataDirectInitializer</c> onto the
/// unified <see cref="KoanModule"/> authoring surface: <see cref="Register"/> attaches direct-mode data
/// services (guarded so an explicit <c>AddKoanDataDirect()</c> isn't double-applied).
/// </summary>
public sealed class KoanDataDirectModule : KoanModule
{
    public override string Id => "Koan.Data.Direct";

    public override void Register(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IDirectDataService)))
        {
            return;
        }

        DirectRegistration.AddKoanDataDirect(services);
    }
}
