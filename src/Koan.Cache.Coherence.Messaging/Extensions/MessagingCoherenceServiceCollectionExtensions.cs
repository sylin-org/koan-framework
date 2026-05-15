using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Coherence.Messaging.Channel;
using Koan.Core.Hosting.App;
using Koan.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Cache.Coherence.Messaging.Extensions;

/// <summary>
/// DI registration for the Koan.Messaging-backed coherence channel.
/// </summary>
public static class MessagingCoherenceServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="MessagingCoherenceChannel"/> as an <see cref="ICacheCoherenceChannel"/>
    /// and wire up the messaging handler that routes incoming envelopes to it.
    /// </summary>
    public static IServiceCollection AddKoanCacheMessagingCoherence(this IServiceCollection services)
    {
        services.TryAddSingleton<MessagingCoherenceChannel>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheCoherenceChannel, MessagingCoherenceChannel>(
                sp => sp.GetRequiredService<MessagingCoherenceChannel>()));

        // Register the messaging handler — it captures AppHost.Current at invocation time
        // to resolve the channel singleton. This sidesteps the chicken-and-egg of needing
        // DI access inside a Func<T,Task> closure registered at ConfigureServices time.
        services.On<MessagingInvalidationEnvelope>(envelope =>
        {
            var sp = AppHost.Current;
            var channel = sp?.GetService<MessagingCoherenceChannel>();
            return channel is null ? Task.CompletedTask : channel.HandleIncoming(envelope);
        });

        return services;
    }
}
