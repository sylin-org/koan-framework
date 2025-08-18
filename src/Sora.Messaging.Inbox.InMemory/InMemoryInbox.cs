using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Messaging;
using System.Collections.Concurrent;

namespace Sora.Messaging.Inbox;

/// Simple in-memory inbox for tests and single-process scenarios.
/// Not suitable for multi-instance deployments.
public sealed class InMemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<string, byte> _processed = new(StringComparer.Ordinal);

    public Task<bool> IsProcessedAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_processed.ContainsKey(key));

    public Task MarkProcessedAsync(string key, CancellationToken ct = default)
    {
        _processed[key] = 1;
        return Task.CompletedTask;
    }
}

public static class InMemoryInboxRegistration
{
    public static IServiceCollection AddInMemoryInbox(this IServiceCollection services)
    {
        services.TryAddSingleton<IInboxStore, InMemoryInboxStore>();
        return services;
    }
}

/// Auto-discovery initializer so AddSora() wires the in-memory inbox when referenced.
// legacy initializer removed in favor of standardized auto-registrar
