using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;
using Koan.Web.Auth.Connector.Test.Options;

namespace Koan.Web.Auth.Connector.Test.Infrastructure;

internal static class TestProviderProvenanceItems
{
    private static readonly TestProviderOptions Defaults = new();

    private static readonly IReadOnlyCollection<string> StartupConsumers = new[]
    {
        "Koan.Web.Auth.Connector.Test.Hosting.KoanTestProviderStartupFilter"
    };

    private static readonly IReadOnlyCollection<string> TokenConsumers = new[]
    {
        "Koan.Web.Auth.Connector.Test.Infrastructure.JwtTokenService"
    };

    private static readonly IReadOnlyCollection<string> ControllerConsumers = new[]
    {
        "Koan.Web.Auth.Connector.Test.Controllers.TokenController"
    };

    internal static readonly ProvenanceItem Enabled = new(
        TestProviderOptions.SectionPath + ":" + nameof(TestProviderOptions.Enabled),
        "Test Provider Enabled",
        "Activator toggle for the test OAuth provider; auto-enabled in Development.",
        DefaultValue: BoolString(Defaults.Enabled),
        DefaultConsumers: StartupConsumers);

    internal static readonly ProvenanceItem TokenFormat = new(
        TestProviderOptions.SectionPath + ":" + nameof(TestProviderOptions.UseJwtTokens),
        "Token Format",
        "Indicates whether issued tokens use JWT or legacy hash format.",
        DefaultValue: Defaults.UseJwtTokens ? "JWT" : "Hash",
        DefaultConsumers: TokenConsumers);

    internal static readonly ProvenanceItem ClientCredentials = new(
        TestProviderOptions.SectionPath + ":" + nameof(TestProviderOptions.EnableClientCredentials),
        "Client Credentials",
        "Enables OAuth client credentials grant for test provider clients.",
        DefaultValue: BoolString(Defaults.EnableClientCredentials),
        DefaultConsumers: ControllerConsumers);

    internal static readonly ProvenanceItem RegisteredClients = new(
        TestProviderOptions.SectionPath + ":" + nameof(TestProviderOptions.RegisteredClients),
        "Registered Clients",
        "Number of OAuth client credentials registered for the test provider.",
        DefaultValue: Defaults.RegisteredClients.Count.ToString(),
        DefaultConsumers: ControllerConsumers);

    internal static readonly ProvenanceItem JwtIssuer = new(
        TestProviderOptions.SectionPath + ":" + nameof(TestProviderOptions.JwtIssuer),
        "JWT Issuer",
        "Issuer claim applied to generated JWT tokens.",
        DefaultValue: Defaults.JwtIssuer,
        DefaultConsumers: TokenConsumers);

    internal static readonly ProvenanceItem JwtAudience = new(
        TestProviderOptions.SectionPath + ":" + nameof(TestProviderOptions.JwtAudience),
        "JWT Audience",
        "Audience claim applied to generated JWT tokens.",
        DefaultValue: Defaults.JwtAudience,
        DefaultConsumers: TokenConsumers);

    internal static readonly ProvenanceItem JwtExpirationMinutes = new(
        TestProviderOptions.SectionPath + ":" + nameof(TestProviderOptions.JwtExpirationMinutes),
        "JWT Expiration Minutes",
        "Lifetime of issued JWT tokens expressed in minutes.",
        DefaultValue: Defaults.JwtExpirationMinutes.ToString(),
        DefaultConsumers: TokenConsumers);

    private static string BoolString(bool value) => value ? "true" : "false";
}
