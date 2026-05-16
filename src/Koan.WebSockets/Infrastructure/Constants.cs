namespace Koan.WebSockets.Infrastructure;

internal static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:Web:WebSockets";

        public static class Keys
        {
            public const string MessageType = "MessageType";
            public const string LeaveOpen = "LeaveOpen";
            public const string SubProtocol = "SubProtocol";
        }
    }
}
