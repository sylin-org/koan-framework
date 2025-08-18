namespace Sora.Messaging.Core.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
    public const string Section = "Sora:Messaging";
    public const string Buses = "Sora:Messaging:Buses";
        public static class Inbox
        {
            public const string Endpoint = "Sora:Messaging:Inbox:Endpoint";
        }
        public static class Discovery
        {
            public const string Enabled = "Sora:Messaging:Discovery:Enabled";
        }
    }
}
