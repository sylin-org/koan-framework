using Koan.Core.Services;
using Constants = Koan.Communication.Connector.RabbitMq.Infrastructure.Constants;

namespace Koan.Communication.Connector.RabbitMq;

[KoanService(ServiceKind.Messaging, shortCode: Constants.Broker.ServiceName, name: "RabbitMQ",
    ContainerImage = Constants.Broker.ContainerImage,
    DefaultTag = Constants.Broker.ContainerTag,
    DefaultPorts = [Constants.Broker.AmqpPort, Constants.Broker.ManagementPort],
    Scheme = "amqp", Host = Constants.Broker.ServiceName, EndpointPort = Constants.Broker.AmqpPort,
    LocalScheme = "amqp", LocalHost = "localhost", LocalPort = Constants.Broker.AmqpPort)]
internal sealed class RabbitMqServiceDescriptor;
