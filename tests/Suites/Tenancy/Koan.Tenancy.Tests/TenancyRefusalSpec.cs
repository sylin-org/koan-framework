using AwesomeAssertions;
using Koan.Tenancy;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0099 §1 — the fail-closed gate's refusal diagnostic is Redis protected-mode quality: it names the
/// offending entity, states why it refused, and lists the three exact remediations (scope / resolver /
/// [HostScoped]) plus the dev-open reminder. The behavioral throw is proven in <see cref="TenantEnforcementSpec"/>.
/// </summary>
public sealed class TenancyRefusalSpec
{
    private static string Msg => TenancyRefusal.NoTenantInScope("Invoice");

    [Fact] public void Names_the_offending_entity() => Msg.Should().Contain("'Invoice'");

    [Fact] public void Keeps_the_stable_opening_phrase() => Msg.Should().StartWith("No tenant in scope");

    [Fact] public void Explains_it_is_a_fail_closed_guard() => Msg.Should().Contain("fails closed").And.Contain("ARCH-0099 §1");

    [Fact] public void Offers_scoping_the_call() => Msg.Should().Contain("Tenant.Use(");

    [Fact] public void Offers_registering_a_resolver() => Msg.Should().Contain("ITenantResolver");

    [Fact] public void Offers_marking_the_entity_host_scoped() => Msg.Should().Contain("[HostScoped]");

    [Fact] public void Reminds_that_development_is_auto_open() => Msg.Should().Contain("Development");
}
