namespace Koan.Data.Connector.Json.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "json";
        internal const string ReferenceIdentity = "Koan.Data.Connector.Json";
        internal const string DefaultSource = "Default";
        internal const int Priority = 0;
        internal const int MaximumFilesPerHost = 1024;
        internal const long MaximumFileBytes = 64L * 1024 * 1024;
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:Json";
        internal const string DefaultSourceSection = "Koan:Data:Sources:Default:json";
        internal const string DirectoryPath = nameof(JsonDataOptions.DirectoryPath);
    }

    internal static class Storage
    {
        internal const string Extension = ".json";
        internal const char PartitionSeparator = '#';
    }

    internal static class Bootstrap
    {
        internal const string DirectoryPath = "data.json.directory";
    }
}
