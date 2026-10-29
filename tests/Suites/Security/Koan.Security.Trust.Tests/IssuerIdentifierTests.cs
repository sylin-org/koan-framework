using System.IdentityModel.Tokens.Jwt;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Security.Trust.Issuer;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0023 — Trust's one issuer name: the one an <see cref="IIssuerIdentifier"/> states, else the named
/// <c>Koan:Security:Trust:Issuer</c>, else <c>koan-dev</c>. Validation accepts the stated name and the named-or-default
/// one, and refuses any other issuer even when the signature is this issuer's.
/// </summary>
public sealed class IssuerIdentifierTests
{
    private sealed class Stated(string? issuer) : IIssuerIdentifier
    {
        public string? Value { get; set; } = issuer;
        public string? Resolve() => Value;
    }

    private static EcdsaIssuer NewIssuer(IIssuerKeyStore store, string? named, IIssuerIdentifier? identifier)
        => new(store, Options.Create(new TrustIssuerOptions { Issuer = named }), NullLogger<EcdsaIssuer>.Instance, identifier);

    private static string Iss(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token).Issuer;

    [Fact]
    public void Without_an_identifier_the_named_issuer_or_the_default_is_stamped()
    {
        var store = new EphemeralIssuerKeyStore();

        Iss(NewIssuer(store, named: null, identifier: null).Issue(new TrustClaims { Subject = "a" })).Should().Be("koan-dev");
        Iss(NewIssuer(store, named: "https://named.example", identifier: null).Issue(new TrustClaims { Subject = "a" }))
            .Should().Be("https://named.example");
    }

    [Fact]
    public void The_stated_issuer_is_stamped_and_accepted_beside_the_default()
    {
        var store = new EphemeralIssuerKeyStore();
        var stated = new Stated("https://app.example/notes");
        var issuer = NewIssuer(store, named: null, stated);

        var token = issuer.Issue(new TrustClaims { Subject = "alice" });
        Iss(token).Should().Be("https://app.example/notes");
        issuer.TryValidate(token, out _).Should().BeTrue();

        // Where nothing can be stated (no request), the default name is stamped, and it is still accepted.
        stated.Value = null;
        var unstated = issuer.Issue(new TrustClaims { Subject = "alice" });
        Iss(unstated).Should().Be("koan-dev");
        stated.Value = "https://app.example/notes";
        issuer.TryValidate(unstated, out _).Should().BeTrue();
    }

    [Fact]
    public void A_token_under_another_issuer_is_refused_even_with_this_issuers_key()
    {
        var store = new EphemeralIssuerKeyStore();
        var stated = new Stated("https://elsewhere.example");
        var foreign = NewIssuer(store, named: null, stated).Issue(new TrustClaims { Subject = "mallory" });

        stated.Value = "https://app.example";
        NewIssuer(store, named: null, stated).TryValidate(foreign, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_named_issuer_that_names_nothing_fails_options_validation(string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Koan:Security:Trust:Issuer"] = value })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        new TrustModule().Register(services);

        using var provider = services.BuildServiceProvider();
        Action resolve = () => _ = provider.GetRequiredService<IOptions<TrustIssuerOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>();
    }
}
