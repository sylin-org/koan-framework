namespace Sora.Web.Infrastructure;

public static class ConfigurationConstants
{
    public static class Web
    {
        public const string Section = "Sora:Web";
        public static class Keys
        {
            public const string EnableSecureHeaders = nameof(EnableSecureHeaders);
            public const string IsProxiedApi = nameof(IsProxiedApi);
            public const string ContentSecurityPolicy = nameof(ContentSecurityPolicy);
            public const string AutoMapControllers = nameof(AutoMapControllers);
        }
    }

}
