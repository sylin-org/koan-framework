using Xunit;

namespace Sora.Mq.RabbitMq.IntegrationTests.Fixtures;

[CollectionDefinition(RabbitMqCollection.Name)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqSharedContainer>
{
    public const string Name = "RabbitMQ(shared)";
}