using Koan.Core.Hosting.App;
using Koan.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Jobs.Transport.Messaging;

/// <summary>DI wiring for the messaging-backed Jobs transport.</summary>
public static class MessagingTransportServiceCollectionExtensions
{
    /// <summary>
    /// Replace the in-process transport with <see cref="MessagingJobTransport"/> and wire the inbound handler.
    /// <c>Replace</c> wins regardless of registrar order (it removes the default if present, else just registers,
    /// and the default's <c>TryAdd</c> then sees this one and stands down).
    /// </summary>
    public static IServiceCollection AddKoanJobsMessagingTransport(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IJobTransport, MessagingJobTransport>());

        // The handler captures AppHost.Current at invocation time to resolve the singleton — sidestepping the
        // chicken-and-egg of needing DI access inside a closure registered at ConfigureServices time.
        services.On<JobReadySignal>(signal =>
        {
            if (AppHost.Current?.GetService<IJobTransport>() is MessagingJobTransport mt) mt.OnRemote(signal);
            return Task.CompletedTask;
        });

        return services;
    }
}
