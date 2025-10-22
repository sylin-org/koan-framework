using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Core.Direct;

namespace Koan.Data.Direct.Initialization;

public sealed class KoanDataDirectInitializer : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IDirectDataService)))
        {
            return;
        }

        DirectRegistration.AddKoanDataDirect(services);
    }
}
