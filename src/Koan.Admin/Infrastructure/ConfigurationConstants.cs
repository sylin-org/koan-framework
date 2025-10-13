namespace Koan.Admin.Infrastructure;

public static class ConfigurationConstants
{
    public static class Admin
    {
        public const string Section = "Koan:Admin";

        public static class Keys
        {
            public const string Enabled = nameof(Enabled);
            public const string EnableConsoleUi = nameof(EnableConsoleUi);
            public const string EnableWeb = nameof(EnableWeb);
            public const string AllowInProduction = nameof(AllowInProduction);
            public const string AllowDotPrefixInProduction = nameof(AllowDotPrefixInProduction);
            public const string PathPrefix = nameof(PathPrefix);
            public const string ExposeManifest = nameof(ExposeManifest);
            public const string DestructiveOps = nameof(DestructiveOps);
        }
    }
}
