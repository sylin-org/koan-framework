using RabbitMQ.Client;

namespace Sora.Messaging.RabbitMq.Provisioning;

public sealed class RabbitMqProviderContext
{
    public string Bus { get; }
    public IConnection Connection { get; }
    public IModel Channel { get; }
    public RabbitMqOptions Options { get; }

    public RabbitMqProviderContext(string bus, IConnection connection, IModel channel, RabbitMqOptions options)
    {
        Bus = bus;
        Connection = connection;
        Channel = channel;
        Options = options;
    }
}
