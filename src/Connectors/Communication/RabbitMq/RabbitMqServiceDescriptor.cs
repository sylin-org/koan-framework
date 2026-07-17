using Koan.Core.Services;

namespace Koan.Communication.Connector.RabbitMq;

[KoanService(ServiceKind.Messaging, shortCode: "rabbitmq", name: "RabbitMQ",
    ContainerImage = "rabbitmq",
    DefaultTag = "3.13-management",
    DefaultPorts = [5672, 15672],
    Scheme = "amqp", Host = "rabbitmq", EndpointPort = 5672,
    LocalScheme = "amqp", LocalHost = "localhost", LocalPort = 5672)]
internal sealed class RabbitMqServiceDescriptor;
