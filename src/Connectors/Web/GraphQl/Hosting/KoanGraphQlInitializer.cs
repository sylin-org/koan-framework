using Microsoft.Extensions.DependencyInjection;
using Koan.Core;

namespace Koan.Web.Connector.GraphQl.Hosting;

internal sealed class KoanGraphQlInitializer : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
        => services.AddKoanGraphQl();
}

