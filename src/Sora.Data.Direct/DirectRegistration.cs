using Microsoft.Extensions.DependencyInjection;

namespace Sora.Data.Direct;

public static class DirectRegistration
{
    public static IServiceCollection AddSoraDataDirect(this IServiceCollection services)
    {
        services.AddSingleton<Sora.Data.Core.Direct.IDirectDataService, DirectDataService>();
        return services;
    }
}