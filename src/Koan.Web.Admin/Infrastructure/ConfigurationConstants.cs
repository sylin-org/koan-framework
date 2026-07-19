using Koan.Web.Admin.Options;

namespace Koan.Web.Admin.Infrastructure;

internal static class ConfigurationConstants
{
    public static class Admin
    {
        public const string Section = "Koan:Admin";

        public static class Keys
        {
            public const string Enabled = nameof(Enabled);
            public const string PathPrefix = nameof(PathPrefix);
        }

        public static class Authorization
        {
            public const string Section = Admin.Section + ":" + nameof(KoanAdminOptions.Authorization);

            public static class Keys
            {
                public const string Policy = nameof(KoanAdminAuthorizationOptions.Policy);
                public const string AutoCreateDevelopmentPolicy = nameof(KoanAdminAuthorizationOptions.AutoCreateDevelopmentPolicy);
            }
        }
    }
}
