namespace Koan.Communication.Connector.RabbitMq.Infrastructure;

internal static class Constants
{
    internal const string ProviderId = "rabbitmq";
    internal const string ProjectReference = "Koan.Communication.Connector.RabbitMq";
    internal const string PackageReference = "Sylin.Koan.Communication.Connector.RabbitMq";

    internal static class Configuration
    {
        internal const string Section = "Koan:Communication:RabbitMq";
        internal const string ConnectionStringName = "RabbitMq";
        internal const string MeshTrustKey = Section + ":MeshTrustKey";
        internal const string Prefetch = Section + ":Prefetch";
        internal const string PublishTimeout = Section + ":PublishTimeout";
        internal const string Username = Section + ":Username";
        internal const string Password = Section + ":Password";
    }

    internal static class Broker
    {
        internal const string ServiceName = "rabbitmq";
        internal const string ContainerImage = "rabbitmq";
        internal const string ContainerTag = "4.3.2-management";
        internal const string ContainerImageReference = ContainerImage + ":" + ContainerTag;
        internal const int AmqpPort = 5672;
        internal const int ManagementPort = 15672;
        internal const int StartupPriority = 250;
        internal const string ExchangePrefix = "koan.communication";
        internal const string ExchangeGeneration = "v3";
        internal const string SignatureHeader = "x-koan-signature-v1";
        internal const string ContentType = "application/vnd.koan.communication+json;v=2";
        internal const string MessageTypePrefix = "koan.communication";
        internal const string MessageGeneration = "v2";
        internal const string TrustDerivationPrefix = "koan.communication.rabbitmq.v1";
        internal const string ClientNamePrefix = "koan.communication";
        internal const string DefaultApplicationSlug = "koan-app";
        internal const string EnvironmentUrl = "RABBITMQ_URL";
        internal const string DefaultUsername = "koan";
        internal const string DefaultPassword = "koan";
        internal const string DependencyTypeEnvironment = "KOAN_DEPENDENCY_TYPE";
        internal const string DefaultUserEnvironment = "RABBITMQ_DEFAULT_USER";
        internal const string DefaultPasswordEnvironment = "RABBITMQ_DEFAULT_PASS";
        internal const string DataPath = "/var/lib/rabbitmq";
        internal const string HealthCheckCommand = "rabbitmq-diagnostics -q ping";
        internal static readonly TimeSpan DiscoveryHealthTimeout = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan NetworkRecoveryInterval = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan OrchestrationHealthTimeout = TimeSpan.FromSeconds(30);
    }

    internal static class Health
    {
        internal const string Name = "communication.rabbitmq";
    }
}
