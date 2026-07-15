namespace Koan.Tenancy.Infrastructure;

internal static class Constants
{
    internal static class ContextCarriage
    {
        public const string AxisKey = "koan:tenant";
        public const string Version = "v1";
        public const string VersionPrefix = Version + ":";
        public const string HostToken = VersionPrefix + "host";
        public const string IdPrefix = VersionPrefix + "id:";
    }
}
