using Microsoft.Extensions.DependencyInjection;
using Koan.Core;

namespace Koan.Data.Core.Initialization;

public sealed class KoanDataCoreInitializer : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        ServiceCollectionExtensions.RegisterKoanDataCoreServices(services);
    }
}
