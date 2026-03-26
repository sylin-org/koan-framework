namespace Koan.Data.Cqrs.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:Cqrs";
        public static class Keys
        {
            public const string DefaultProfile = Section + ":DefaultProfile";
        }
        public static class Profiles
        {
            public const string Section = Configuration.Section + ":Profiles";
        }
        public static class Outbox
        {
            /// <summary>
            /// Pattern: Koan:Cqrs:Outbox:{adapterName}
            /// </summary>
            public static string ForAdapter(string adapterName) => $"{Configuration.Section}:Outbox:{adapterName}";
        }

        public static class DataSources
        {
            /// <summary>
            /// Pattern: Koan:Data:Sources:{name}:{provider}:ConnectionString
            /// </summary>
            public static string ConnectionString(string name, string provider) =>
                $"Koan:Data:Sources:{name}:{provider}:ConnectionString";
        }
    }
}
