namespace Sora.Messaging;

public interface IMessageBus
{
    Task SendAsync(object message, CancellationToken ct = default);
    Task SendManyAsync(IEnumerable<object> messages, CancellationToken ct = default);
}