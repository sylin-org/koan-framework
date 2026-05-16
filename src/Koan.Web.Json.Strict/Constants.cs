namespace Koan.Web.Json.Strict;

internal static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:Json:MinimalApis";

        public static class Keys
        {
            public const string Strict = "Strict";
            public const string AllowDuplicateProperties = "AllowDuplicateProperties";
            public const string CombineRegisteredResolvers = "CombineRegisteredResolvers";
        }
    }
}
