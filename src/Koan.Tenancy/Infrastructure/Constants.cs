namespace Koan.Tenancy.Infrastructure;

internal static class Constants
{
    internal static class Segmentation
    {
        public const string DimensionId = "tenant";
        public const string Correction =
            "Establish a trusted tenant context with Tenant.Use(\"<tenantId>\") or an ITenantResolver. " +
            "If the subject is genuinely control-plane data, mark it [HostScoped] and use an explicit host scope.";
    }

    internal static class ContextCarriage
    {
        public const string AxisKey = "koan:tenant";
        public const string Version = "v1";
        public const string VersionPrefix = Version + ":";
        public const string HostToken = VersionPrefix + "host";
        public const string IdPrefix = VersionPrefix + "id:";
    }
}
