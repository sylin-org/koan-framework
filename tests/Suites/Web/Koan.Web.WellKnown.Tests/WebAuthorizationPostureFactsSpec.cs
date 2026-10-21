using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// NK-055 / SEC-0020. Allow is the deliberate default for an entity or verb nothing declares; the posture it
/// resolves to is stated in the host's runtime facts so an operator reads it instead of inferring it.
/// </summary>
public sealed class WebAuthorizationPostureFactsSpec
{
    private const string Code = "koan.web.authorization.default";

    [Fact]
    public async Task The_open_default_is_stated_in_the_facts()
    {
        var fact = await DefaultFact(new Dictionary<string, string?>());

        fact.Subject.Should().Be("web:authorization");
        fact.Summary.Should().Contain("Allow").And.Contain("Koan:Web:Authorization:DefaultDecision");
    }

    [Fact]
    public async Task A_narrowed_default_is_stated_as_forbid()
    {
        var fact = await DefaultFact(new Dictionary<string, string?> { ["Koan:Web:Authorization:DefaultDecision"] = "Forbid" });

        fact.Summary.Should().Contain("Forbid").And.NotContain("Allow");
    }

    private static async Task<KoanFact> DefaultFact(IReadOnlyDictionary<string, string?> settings)
    {
        var factory = new WellKnownWebApplicationFactory { Settings = settings };
        await factory.InitializeAsync();
        try
        {
            var facts = factory.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;
            return facts.Should().ContainSingle(fact => fact.Code == Code).Subject;
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }
}
