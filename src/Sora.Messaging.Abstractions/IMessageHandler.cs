namespace Sora.Messaging;

public interface IMessageHandler<T>
{
    Task HandleAsync(MessageEnvelope envelope, T message, CancellationToken ct);
}