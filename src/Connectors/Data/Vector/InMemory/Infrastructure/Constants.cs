namespace Koan.Data.Vector.Connector.InMemory.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        public const string Name = "inmemory";
        public const int Priority = -100;
        public static readonly IReadOnlyCollection<string> Aliases = Array.AsReadOnly(["memory", "inproc"]);
    }

    internal static class Configuration
    {
        public const string Section = "Koan:Data:Vector:InMemory";
    }

    internal static class Defaults
    {
        public const int MaxSpaces = 256;
        public const int MaxPointsPerSpace = 100_000;
        public const int MaxDimensions = 16_384;
        public const int MaxMetadataBytesPerPoint = 1_048_576;
    }
}
