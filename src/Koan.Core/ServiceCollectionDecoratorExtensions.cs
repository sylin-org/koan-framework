using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core;

public static class ServiceCollectionDecoratorExtensions
{
    // Minimal open-generic decorator helper (subset). It replaces existing service descriptors for the given open-generic service
    // with a factory that constructs the decorator and resolves the previous implementation.
    public static IServiceCollection TryDecorate(this IServiceCollection services, Type serviceOpenGeneric, Type decoratorOpenGeneric)
    {
        var matches = services.Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == serviceOpenGeneric).ToList();
        foreach (var d in matches)
        {
            var genericArgs = d.ServiceType.GetGenericArguments();
            var closedDecorator = decoratorOpenGeneric.MakeGenericType(genericArgs);
            var closedService = d.ServiceType;
            var closedImpl = d.ImplementationType ?? d.ImplementationInstance?.GetType();
            services.Remove(d);
            services.Add(new ServiceDescriptor(closedService, sp =>
            {
                var inner = d.ImplementationFactory != null ? d.ImplementationFactory(sp) : (closedImpl != null ? ActivatorUtilities.CreateInstance(sp, closedImpl) : sp.GetRequiredService(closedService));
                return ActivatorUtilities.CreateInstance(sp, closedDecorator, inner);
            }, d.Lifetime));
        }
        return services;
    }
}
