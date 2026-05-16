using System.Threading.Tasks;
using Koan.Cache.Coherence.Messaging.Channel;
using Koan.Cache.Abstractions.Extensions;
using Koan.Core.Hosting.App;
using Koan.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Cache.Coherence.Messaging.Extensions;

/// <summary>
/// DI registration for the Koan.Messaging-backed coherence channel.
/// </summary>
public static class MessagingCoherenceServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="MessagingCoherenceChannel"/> as an <c>ICacheCoherenceChannel</c>
    /// and wire up the messaging handler that routes incoming envelopes to it.
    /// </summary>
    public static IServiceCollection AddKoanCacheMessagingCoherence(this IServiceCollection services)
    {
        services.AddCoherenceChannel<MessagingCoherenceChannel>();

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
