using System;
using Microsoft.Extensions.DependencyInjection;
using Sora.Messaging;
using Sora.Messaging.Core;

namespace S8.Flow.Shared.Commands;

public static class FlowCommandServiceCollectionExtensions
{
    public static IServiceCollection AddFlowCommands(this IServiceCollection services, Action<IFlowCommandRegistry> configure)
    {
        var router = new FlowCommandRouter();
        configure(router);
        services.AddSingleton<IFlowCommandRegistry>(router);
        services.OnMessages(h => h.On<FlowCommandDispatch>(async (env, msg, ct) =>
        {
            // Use AppHost.Current as the service provider
            var sp = Sora.Core.Hosting.App.AppHost.Current;
            if (sp == null)
                return;
            var registry = sp.GetService(typeof(IFlowCommandRegistry)) as FlowCommandRouter;
            if (registry != null)
                await registry.DispatchAsync(msg, sp, ct);
        }));
        return services;
    }
}
