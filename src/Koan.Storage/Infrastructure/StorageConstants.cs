namespace Koan.Storage.Infrastructure;

public static class StorageConstants
{
    public const string DefaultProfile = "default";

    public static class Constants
    {
        public static class Configuration
        {
            public const string Section = "Koan:Storage";
            public static class Keys
            {
                public const string Profiles = nameof(Profiles);
                public const string DefaultProfile = nameof(DefaultProfile);
                public const string FallbackMode = nameof(FallbackMode);
                public const string ValidateOnStart = nameof(ValidateOnStart);
            }
        }
    }
}
