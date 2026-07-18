using Koan.Core.Hosting.Bootstrap;
using Koan.Web.OpenApi.Options;

namespace Koan.Web.OpenApi.Infrastructure;

/// <summary>
/// Centralized constants for Koan's OpenAPI integration.
/// </summary>
internal static class Constants
{
    public static class Configuration
    {
        public const string Section = KoanOpenApiOptions.SectionPath;

        public static class Keys
        {
            public const string Enabled = nameof(Enabled);
            public const string EnableUi = nameof(EnableUi);
            public const string RoutePattern = nameof(RoutePattern);
            public const string UiRoute = nameof(UiRoute);
            public const string RequireAuthenticationOutsideDevelopment = nameof(RequireAuthenticationOutsideDevelopment);
        }

        public const string Enabled = Section + ":" + Keys.Enabled;
        public const string EnableUi = Section + ":" + Keys.EnableUi;
        public const string RoutePattern = Section + ":" + Keys.RoutePattern;
        public const string UiRoute = Section + ":" + Keys.UiRoute;
        public const string RequireAuthenticationOutsideDevelopment = Section + ":" + Keys.RequireAuthenticationOutsideDevelopment;
    }

    public static class Runtime
    {
        public const string AppliedKey = "Koan.Web.OpenApi.Applied";
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

        public static readonly ProvenanceItem Enabled = new(
            "OpenApiEnabled",
            "OpenAPI Document Enabled",
            "Whether the referenced OpenAPI package publishes its document endpoint.");

        public static readonly ProvenanceItem Ui = new(
            "OpenApiUi",
            "OpenAPI UI",
            "Effective interactive UI route and authentication posture.");
    }
}
