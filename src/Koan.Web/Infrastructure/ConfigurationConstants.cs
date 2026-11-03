namespace Koan.Web.Infrastructure;

public static class ConfigurationConstants
{
    public static class Web
    {
        public const string Section = "Koan:Web";
        public static class Keys
        {
            public const string EnableSecureHeaders = nameof(EnableSecureHeaders);
            public const string IsProxiedApi = nameof(IsProxiedApi);
            public const string ContentSecurityPolicy = nameof(ContentSecurityPolicy);
            public const string AutoMapControllers = nameof(AutoMapControllers);
            public const string ApplicationUrl = nameof(ApplicationUrl);
            public const string TrustedProxies = nameof(TrustedProxies);
            public const string ObservedOriginLimit = nameof(ObservedOriginLimit);
            public const string KeyRingDirectory = nameof(KeyRingDirectory);
            public const string KeyRingApplicationName = nameof(KeyRingApplicationName);
            public const string RefuseCrossSiteWrites = nameof(RefuseCrossSiteWrites);
        }
    }

}
