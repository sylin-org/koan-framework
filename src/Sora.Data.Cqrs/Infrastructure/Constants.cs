namespace Sora.Data.Cqrs.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public const string Section = "Sora:Cqrs";
        public static class Keys
        {
            public const string DefaultProfile = Section + ":DefaultProfile";
        }
        public static class Profiles
        {
            public const string Section = Configuration.Section + ":Profiles";
        }
    }
}
