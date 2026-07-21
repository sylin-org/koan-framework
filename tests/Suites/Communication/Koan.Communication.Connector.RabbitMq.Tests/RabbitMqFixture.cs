using Testcontainers.RabbitMq;

namespace Koan.Communication.Connector.RabbitMq.Tests;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The RabbitMQ test container has not started.");

    public async ValueTask InitializeAsync()
    {
        _container = new RabbitMqBuilder("rabbitmq:4.3.2-alpine").Build();
        await _container.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync().ConfigureAwait(false);
    }
}
