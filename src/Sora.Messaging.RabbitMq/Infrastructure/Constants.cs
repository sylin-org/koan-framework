namespace Sora.Messaging.RabbitMq.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        // Base path for a specific busCode's RabbitMq settings under Sora:Messaging:Buses:{code}:RabbitMq
        public const string Buses = "Sora:Messaging:Buses";
        public static string Exchange(string busCode) => $"{Buses}:{busCode}:RabbitMq:Exchange";
    public static string ExchangeType(string busCode) => $"{Buses}:{busCode}:RabbitMq:ExchangeType";
        public static string ConnectionString(string busCode) => $"{Buses}:{busCode}:RabbitMq:ConnectionString";
        public static string ConnectionStringName(string busCode) => $"{Buses}:{busCode}:RabbitMq:ConnectionStringName";
    }
}
