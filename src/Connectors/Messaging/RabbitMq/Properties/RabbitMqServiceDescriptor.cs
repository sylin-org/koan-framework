using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Messaging.Connector.RabbitMq;

/// <summary>
/// Orchestration identity for the RabbitMQ messaging connector.
/// Carries the connector's <see cref="KoanServiceAttribute"/> so
/// <see cref="Discovery.RabbitMqDiscoveryAdapter"/> can resolve host/port/scheme
/// without baking that knowledge into the provider (mirrors the LMStudioServiceDescriptor
/// precedent: a dedicated descriptor type keeps the provider clean — ARCH-0087).
/// </summary>
[KoanService(ServiceKind.Messaging, shortCode: "rabbitmq", name: "RabbitMQ",
    ContainerImage = "rabbitmq",
    DefaultTag = "3.13-management",
    DefaultPorts = new[] { 5672, 15672 },
    Scheme = "amqp", Host = "rabbitmq", EndpointPort = 5672,
    LocalScheme = "amqp", LocalHost = "localhost", LocalPort = 5672)]
internal sealed class RabbitMqServiceDescriptor
{
}
