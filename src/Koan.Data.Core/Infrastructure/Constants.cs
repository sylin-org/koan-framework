namespace Koan.Data.Core.Infrastructure;

public static class Constants
{
    public static class Defaults
    {
        // Default page size used by facade loops when materializing "All"/"QueryAll" across providers.
        // Keep conservative to balance throughput and memory. Adapters no longer clamp to their own
        // MaxPageSize (that cap was removed); request-time output-layer policy is the right boundary.
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
