namespace Koan.Data.Vector.Infrastructure;

internal static class Constants
{
    internal static class Configuration
    {
        public const string DefaultsSection = "Koan:Data:VectorDefaults";

        internal static class Keys
        {
            public const string DefaultProvider = "Koan:Data:VectorDefaults:DefaultProvider";
        }
    }

    internal static class Defaults
    {
        public const int RepositoryEntries = 256;
        public const int MetadataShapeEntries = 256;
        public const int MaxMetadataDepth = 16;
        public const int MaxTop = 10_000;
    }
}
