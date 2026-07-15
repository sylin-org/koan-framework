namespace Koan.Data.Access.Infrastructure;

internal static class Constants
{
    internal static class ContextCarriage
    {
        public const string AxisKey = "koan:subject";
        public const string Version = "v1";
        public const string VersionPrefix = Version + ":";
        public const string SystemToken = VersionPrefix + "system";
        public const string IdPrefix = VersionPrefix + "id:";
        public const string ScopedPrefix = VersionPrefix + "scoped:";
        public const char UnitSeparator = '\u001f';
    }
}
