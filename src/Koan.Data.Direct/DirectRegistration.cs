using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Direct;

public static class DirectRegistration
{
    public static IServiceCollection AddKoanDataDirect(this IServiceCollection services)
    {
        services.AddSingleton<Koan.Data.Core.Direct.IDirectDataService, DirectDataService>();
        return services;
    }
}