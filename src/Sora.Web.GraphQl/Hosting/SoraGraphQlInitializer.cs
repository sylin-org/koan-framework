using Microsoft.Extensions.DependencyInjection;
using Sora.Core;

namespace Sora.Web.GraphQl.Hosting;

internal sealed class SoraGraphQlInitializer : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
        => services.AddSoraGraphQl();
}
