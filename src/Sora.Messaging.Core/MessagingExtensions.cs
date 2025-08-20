using Microsoft.Extensions.DependencyInjection;
using Sora.Core;

namespace Sora.Messaging;

public static class MessagingExtensions
{
    public static Task Send(this object message, CancellationToken ct = default)
        => Resolve().SendAsync(message, ct);

    public static Task SendTo(this object message, string busCode, CancellationToken ct = default)
        => Resolve(busCode).SendAsync(message, ct);

    public static Task Send<T>(this IEnumerable<T> messages, CancellationToken ct = default)
        => Resolve().SendManyAsync(messages.Cast<object>(), ct);

    public static Task SendTo<T>(this IEnumerable<T> messages, string busCode, CancellationToken ct = default)
        => Resolve(busCode).SendManyAsync(messages.Cast<object>(), ct);

    // Explicit overloads for discoverability with non-generic collections
    public static Task Send(this IEnumerable<object> messages, CancellationToken ct = default)
        => Resolve().SendManyAsync(messages, ct);

    public static Task SendTo(this IEnumerable<object> messages, string busCode, CancellationToken ct = default)
        => Resolve(busCode).SendManyAsync(messages, ct);

    // Send a single grouped message (Batch<T>) for explicit batch semantics
    public static Task SendBatch<T>(this IEnumerable<T> items, CancellationToken ct = default)
        => Resolve().SendAsync(new Batch<T>(items), ct);

    public static Task SendBatchTo<T>(this IEnumerable<T> items, string busCode, CancellationToken ct = default)
        => Resolve(busCode).SendAsync(new Batch<T>(items), ct);

    // Readability aliases
    public static Task SendAsBatch<T>(this IEnumerable<T> items, CancellationToken ct = default)
        => items.SendBatch(ct);

    public static Task SendAsBatchTo<T>(this IEnumerable<T> items, string busCode, CancellationToken ct = default)
        => items.SendBatchTo(busCode, ct);

    // OnMessage sugar: register a delegate-based handler via DI.
    public static IServiceCollection OnMessage<T>(this IServiceCollection services, Func<MessageEnvelope, T, CancellationToken, Task> handler)
    {
        services.AddSingleton<IMessageHandler<T>>(sp => new DelegateMessageHandler<T>(handler));
        return services;
    }

    public static IServiceCollection OnMessage<T>(this IServiceCollection services, Action<MessageEnvelope, T> handler)
    {
        services.AddSingleton<IMessageHandler<T>>(sp => new DelegateMessageHandler<T>((env, msg, ct) => { handler(env, msg); return Task.CompletedTask; }));
        return services;
    }

    // Register multiple handlers in one fluent block
    public static IServiceCollection OnMessages(this IServiceCollection services, Action<MessageHandlerBuilder> configure)
    {
        var builder = new MessageHandlerBuilder(services);
        configure(builder);
        return services;
    }

    // Fluent builder for handler registration
    public sealed class MessageHandlerBuilder
    {
        private readonly IServiceCollection _services;
        public MessageHandlerBuilder(IServiceCollection services) => _services = services;

        public MessageHandlerBuilder On<T>(Func<MessageEnvelope, T, CancellationToken, Task> handler)
        { _services.OnMessage<T>(handler); return this; }

        public MessageHandlerBuilder On<T>(Action<MessageEnvelope, T> handler)
        { _services.OnMessage<T>(handler); return this; }

        // Convenience for grouped batches
        public MessageHandlerBuilder OnBatch<T>(Func<MessageEnvelope, Batch<T>, CancellationToken, Task> handler)
        { _services.OnBatch(handler); return this; }

        public MessageHandlerBuilder OnBatch<T>(Action<MessageEnvelope, Batch<T>> handler)
        { _services.OnBatch(handler); return this; }

        public MessageHandlerBuilder OnMessages<T>(Func<MessageEnvelope, IReadOnlyList<T>, CancellationToken, Task> handler)
        { _services.OnMessages(handler); return this; }

        public MessageHandlerBuilder OnMessages<T>(Action<MessageEnvelope, IReadOnlyList<T>> handler)
        { _services.OnMessages(handler); return this; }
    }

    // Delegate sugar for Batch<T>
    public static IServiceCollection OnBatch<T>(this IServiceCollection services, Func<MessageEnvelope, Batch<T>, CancellationToken, Task> handler)
    {
        services.AddSingleton<IMessageHandler<Batch<T>>>(sp => new DelegateMessageHandler<Batch<T>>(handler));
        return services;
    }

    public static IServiceCollection OnBatch<T>(this IServiceCollection services, Action<MessageEnvelope, Batch<T>> handler)
    {
        services.AddSingleton<IMessageHandler<Batch<T>>>(sp => new DelegateMessageHandler<Batch<T>>((env, msg, ct) => { handler(env, msg); return Task.CompletedTask; }));
        return services;
    }

    // Sugar: handle grouped messages as IReadOnlyList<T> without dealing with Batch<T>
    public static IServiceCollection OnMessages<T>(this IServiceCollection services, Func<MessageEnvelope, IReadOnlyList<T>, CancellationToken, Task> handler)
        => services.OnBatch<T>((env, batch, ct) => handler(env, batch.Items, ct));

    public static IServiceCollection OnMessages<T>(this IServiceCollection services, Action<MessageEnvelope, IReadOnlyList<T>> handler)
        => services.OnBatch<T>((env, batch) => handler(env, batch.Items));

    private sealed class DelegateMessageHandler<T> : IMessageHandler<T>
    {
        private readonly Func<MessageEnvelope, T, CancellationToken, Task> _handler;
        public DelegateMessageHandler(Func<MessageEnvelope, T, CancellationToken, Task> handler) => _handler = handler;
        public Task HandleAsync(MessageEnvelope envelope, T message, CancellationToken ct) => _handler(envelope, message, ct);
    }
    private static IMessageBus Resolve(string? busCode = null)
    {
        var sp = SoraApp.Current ?? throw new InvalidOperationException("SoraApp.Current is not set. Call services.AddSora(); then provider.UseSora() during startup.");
        var sel = (IMessageBusSelector?)sp.GetService(typeof(IMessageBusSelector))
            ?? throw new InvalidOperationException("Messaging is not configured. Reference a provider package or register explicitly.");
        return busCode is null ? sel.ResolveDefault(sp) : sel.Resolve(sp, busCode);
    }
}
