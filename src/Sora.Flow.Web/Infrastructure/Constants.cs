namespace Sora.Flow.Web.Infrastructure;

internal static class WebConstants
{
    public static class Routes
    {
        public const string DefaultPrefix = "/api/flow";
    }

    public static class Control
    {
        public const string Model = "controlcommand"; // FlowAck/ControlResponse model label

        public static class Verbs
        {
            public const string Announce = "announce";
            public const string Ping = "ping";
        }

        public static class Config
        {
            private const string Base = "Sora:Flow:Control:AutoResponse";
            public const string AnnounceEnabled = Base + ":Announce:Enabled"; // bool
            public const string PingEnabled = Base + ":Ping:Enabled"; // bool
        }

        public static class Status
        {
            public const string Ok = "ok";
            public const string Unsupported = "unsupported";
            public const string Error = "error";
            public const string NotFound = "not-found";
        }

        public static class Messages
        {
            public const string TargetRequired = "Target (system:adapter) required";
            public const string RegistryUnavailable = "Adapter registry unavailable";
            public static string NoAdapterMatched(string target) => $"No adapter matched '{target}'";
        }
    }
}
