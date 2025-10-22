namespace Koan.Data.Core.Infrastructure;

public static class Constants
{
    public static class Defaults
    {
        // Default page size used by facade loops when materializing "All"/"QueryAll" across providers
        // Keep conservative to balance throughput and memory; adapters may clamp to their MaxPageSize.
        public const int UnboundedLoopPageSize = 1000;
    }
    public static class Configuration
    {
        public static class Direct
        {
            public const string Section = "Koan:Data:Direct";
        }
    }
}
