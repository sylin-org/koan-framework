using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.OpenApi.Infrastructure;

/// <summary>
/// Centralized constants for Koan's OpenAPI integration.
/// </summary>
public static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:OpenApi";
        public const string Enabled = "Koan:OpenApi:Enabled";
        public static class Keys
        {
            public const string Enabled = nameof(Enabled);
            public const string RoutePattern = nameof(RoutePattern);
        }
    }

    public static class Provenance
    {
        public static readonly ProvenanceItem SpecVersion = new(
            "OpenApiSpecVersion",
            "OpenAPI Specification Version",
            "Spec version emitted by Koan.Web.OpenApi.");

        public static readonly ProvenanceItem Route = new(
            "OpenApiRoute",
            "OpenAPI Route",
            "Resolved HTTP route exposing the OpenAPI document.");
    }
}
