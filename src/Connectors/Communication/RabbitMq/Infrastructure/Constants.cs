namespace Koan.Communication.Connector.RabbitMq.Infrastructure;

internal static class Constants
{
    internal const string ProviderId = "rabbitmq";
    internal const string ProjectReference = "Koan.Communication.Connector.RabbitMq";
    internal const string PackageReference = "Sylin.Koan.Communication.Connector.RabbitMq";

    internal static class Configuration
    {
        internal const string Section = "Koan:Communication:RabbitMq";
        internal const string ConnectionString = Section + ":ConnectionString";
        internal const string MeshTrustKey = Section + ":MeshTrustKey";
        internal const string Prefetch = Section + ":Prefetch";
        internal const string PublishTimeout = Section + ":PublishTimeout";
        internal const string Username = Section + ":Username";
        internal const string Password = Section + ":Password";
        internal const string LegacyConnectionString = "Koan:Messaging:RabbitMQ:ConnectionString";
        internal const string LegacyFallbackConnectionString = "Koan:Messaging:ConnectionString";
    }

    internal static class Broker
    {
        internal const string ServiceName = "rabbitmq";
        internal const string ExchangePrefix = "koan.communication";
        internal const string SignatureHeader = "x-koan-signature-v1";
        internal const string ContentType = "application/vnd.koan.communication+json;v=2";
        internal const string MessageTypePrefix = "koan.communication";
        internal const string EnvironmentUrl = "RABBITMQ_URL";
        internal const string KoanEnvironmentUrl = "Koan_RABBITMQ_URL";
        internal const string DefaultUsername = "koan";
        internal const string DefaultPassword = "koan";
    }

    internal static class Health
    {
        internal const string Name = "communication.rabbitmq";
    }
}
