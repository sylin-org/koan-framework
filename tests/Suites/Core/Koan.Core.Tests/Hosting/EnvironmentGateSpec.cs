using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Core.Tests.Hosting;

/// <summary>
/// The law every capability gate follows, pinned once here so no call site has to re-derive it: Production is
/// the gate, not Development. Each of these cases corresponds to a real drift found in the tree — Staging
/// blocked as if it were Production, and a documented escape hatch no call site consulted.
/// </summary>
public sealed class EnvironmentGateSpec
{
    private static readonly KoanMagic Sample = new(
        Capability: "relational DDL",
        Risk: "schema changes are applied directly to whatever database the connection string points at",
        Remedy: "provision the schema out of band, or set AllowProductionDdl on the source");

    [Theory(DisplayName = "every environment below Production runs the convenience")]
    [InlineData("Development", MagicVerdict.Allowed)]
    [InlineData("Staging", MagicVerdict.AllowedWithNotice)]
    [InlineData("Test", MagicVerdict.AllowedWithNotice)]
    [InlineData("", MagicVerdict.AllowedWithNotice)]
    public void Below_production_the_convenience_runs(string environment, MagicVerdict expected)
    {
        // Gating on IsDevelopment() instead would refuse Test, Staging and CI, which is a functionality block
        // wearing a safety rail's clothes.
        KoanEnv.Gate.Evaluate(Sample, Env(environment)).Should().Be(expected);
        KoanEnv.Gate.Allows(Sample, Env(environment)).Should().BeTrue();
    }

    [Fact(DisplayName = "Production refuses without consent")]
    public void Production_refuses_without_consent()
    {
        KoanEnv.Gate.Evaluate(Sample, Env(Environments.Production)).Should().Be(MagicVerdict.Refused);
        KoanEnv.Gate.Allows(Sample, Env(Environments.Production)).Should().BeFalse();
    }

    [Fact(DisplayName = "the capability's own consent unlocks Production")]
    public void Capability_consent_unlocks_production()
    {
        var consented = Sample with { Consent = true };

        KoanEnv.Gate.Evaluate(consented, Env(Environments.Production))
            .Should().Be(MagicVerdict.AllowedByConsent);
    }

    [Fact(DisplayName = "a refusal names the capability, the risk, the remedy, and the escape hatch")]
    public void Refusal_explains_itself()
    {
        var refuse = () => KoanEnv.Gate.Enforce(Sample, Env(Environments.Production));

        // A refusal an operator cannot act on is just an outage. Each clause earns its place.
        refuse.Should().Throw<InvalidOperationException>()
            .WithMessage("*refuses relational DDL in Production*")
            .WithMessage("*connection string points at*")
            .WithMessage("*AllowProductionDdl*")
            .WithMessage("*Koan:AllowMagicInProduction=true*");
    }

    [Fact(DisplayName = "Enforce lets every non-Production environment through")]
    public void Enforce_is_silent_below_production()
    {
        var run = () => KoanEnv.Gate.Enforce(Sample, Env(Environments.Staging));
        run.Should().NotThrow();
    }

    [Fact(DisplayName = "Announce reports refusal instead of throwing")]
    public void Announce_never_throws()
    {
        KoanEnv.Gate.Announce(Sample, logger: null, environment: Env(Environments.Production))
            .Should().BeFalse("skipping is a coherent outcome for a discovery-shaped convenience");
        KoanEnv.Gate.Announce(Sample, logger: null, environment: Env(Environments.Staging))
            .Should().BeTrue();
    }

    [Fact(DisplayName = "development-only surfaces exist only in Development")]
    public void Development_only_is_exclusive_to_development()
    {
        KoanEnv.Gate.DevelopmentOnly(Env(Environments.Development)).Should().BeTrue();
        KoanEnv.Gate.DevelopmentOnly(Env(Environments.Staging)).Should().BeFalse();
        KoanEnv.Gate.DevelopmentOnly(Env(Environments.Production)).Should().BeFalse();
    }

    private static IHostEnvironment Env(string name) => new StubEnvironment(name);

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Koan.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
