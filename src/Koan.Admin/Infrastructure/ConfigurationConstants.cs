using Koan.Admin.Options;

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
            public const string EnableLaunchKit = nameof(EnableLaunchKit);
            public const string AllowInProduction = nameof(AllowInProduction);
            public const string AllowDotPrefixInProduction = nameof(AllowDotPrefixInProduction);
            public const string PathPrefix = nameof(PathPrefix);
            public const string ExposeManifest = nameof(ExposeManifest);
            public const string DestructiveOps = nameof(DestructiveOps);
            public const string Generate = nameof(KoanAdminOptions.Generate);
        }

        public static class Authorization
        {
            public const string Section = Admin.Section + ":" + nameof(KoanAdminOptions.Authorization);

            public static class Keys
            {
                public const string Policy = nameof(KoanAdminAuthorizationOptions.Policy);
                public const string AllowedNetworks = nameof(KoanAdminAuthorizationOptions.AllowedNetworks);
                public const string AutoCreateDevelopmentPolicy = nameof(KoanAdminAuthorizationOptions.AutoCreateDevelopmentPolicy);
            }
        }

        public static class Logging
        {
            public const string Section = Admin.Section + ":" + nameof(KoanAdminOptions.Logging);

            public static class Keys
            {
                public const string EnableLogStream = nameof(KoanAdminLoggingOptions.EnableLogStream);
                public const string AllowTranscriptDownload = nameof(KoanAdminLoggingOptions.AllowTranscriptDownload);
                public const string AllowedCategories = nameof(KoanAdminLoggingOptions.AllowedCategories);
            }
        }

        public static class Generate
        {
            public const string Section = Admin.Section + ":" + nameof(KoanAdminOptions.Generate);

            public static class Keys
            {
                public const string ComposeProfiles = nameof(KoanAdminGenerateOptions.ComposeProfiles);
                public const string OpenApiClients = nameof(KoanAdminGenerateOptions.OpenApiClients);
                public const string IncludeAppSettings = nameof(KoanAdminGenerateOptions.IncludeAppSettings);
                public const string IncludeCompose = nameof(KoanAdminGenerateOptions.IncludeCompose);
                public const string IncludeAspire = nameof(KoanAdminGenerateOptions.IncludeAspire);
                public const string IncludeManifest = nameof(KoanAdminGenerateOptions.IncludeManifest);
                public const string IncludeReadme = nameof(KoanAdminGenerateOptions.IncludeReadme);
                public const string ComposeBasePort = nameof(KoanAdminGenerateOptions.ComposeBasePort);
            }
        }
    }
}
