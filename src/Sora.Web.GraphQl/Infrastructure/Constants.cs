namespace Sora.Web.GraphQl.Infrastructure;

internal static class Constants
{
    internal static class Configuration
    {
        // Canonical section for typed options
        public const string Section = "Sora:Web:GraphQl";

        // Individual keys (kept for targeted reads if needed)
        public const string Enabled = Section + ":Enabled";
        public const string Path = Section + ":Path"; // default "/graphql"
        public const string AllowAltPlayground = Section + ":Playground"; // bool
        public const string Debug = Section + ":Debug"; // bool
    }
}
