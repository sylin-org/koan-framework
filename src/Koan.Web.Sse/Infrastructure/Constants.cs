namespace Koan.Web.Sse.Infrastructure;

internal static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:Web:Sse";

        public static class Keys
        {
            public const string Enabled = "Enabled";
            public const string DefaultEvent = "DefaultEvent";
            public const string HeartbeatInterval = "HeartbeatInterval";
        }
    }
}
