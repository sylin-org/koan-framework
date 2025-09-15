using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Cqrs;

public static class InMemoryOutboxRegistration
{
    public static IServiceCollection AddInMemoryOutbox(this IServiceCollection services)
    {
        services.BindOutboxOptions<InMemoryOutboxOptions>("InMemory");
        services.AddSingleton<IOutboxStore, InMemoryOutboxStore>();
        return services;
    }
}