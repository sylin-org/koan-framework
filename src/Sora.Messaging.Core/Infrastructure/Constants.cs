namespace Sora.Messaging.Core.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public const string Section = "Sora:Messaging";
        public const string Buses = "Sora:Messaging:Buses";
        public static class Keys
        {
            public const string DefaultBus = Section + ":DefaultBus";
            public const string DefaultGroup = Section + ":DefaultGroup";
            public const string IncludeVersionInAlias = Section + ":IncludeVersionInAlias";
        }
        public static class Inbox
        {
            public const string Endpoint = "Sora:Messaging:Inbox:Endpoint";
            public static class Routes
            {
                public const string GetStatus = "/v1/inbox/{key}";
                public const string MarkProcessed = "/v1/inbox/mark-processed";
            }
            public static class Values
            {
                public const string Processed = "Processed";
                public const int DefaultTimeoutSeconds = 5;
            }
        }
        public static class Discovery
        {
            public const string Enabled = "Sora:Messaging:Discovery:Enabled";
        }
    }
}
