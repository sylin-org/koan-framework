using System;
using Microsoft.Extensions.DependencyInjection;
using Sora.Messaging;
// [REMOVED obsolete Sora.Messaging.Core using]

namespace S8.Flow.Shared.Commands;

public static class FlowCommandServiceCollectionExtensions
{
    public static IServiceCollection AddFlowCommands(this IServiceCollection services, Action<IFlowCommandRegistry> configure)
    {
        var router = new FlowCommandRouter();
        configure(router);
        services.AddSingleton<IFlowCommandRegistry>(router);
    // [REMOVED: OnMessages extension is obsolete or missing]
        return services;
    }
}
