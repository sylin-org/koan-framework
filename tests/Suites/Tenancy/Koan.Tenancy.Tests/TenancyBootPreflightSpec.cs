using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Tenancy;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0099 §1 — the boot pre-flight WIRING, through a real <c>AddKoan()</c> boot (ARCH-0079). Proves the
/// production host refuses to boot (the tenancy module's <c>Start</c> throws, aborting startup) when a resolver
/// is missing or a dev-branded secret leaked into config, and boots cleanly once a resolver is registered or the
/// environment is non-production. The prod signal is the per-host <c>IHostEnvironment</c>, so each case is
/// controllable without the process-global <c>KoanEnv</c> latch.
/// </summary>
public sealed class TenancyBootPreflightSpec
{
    private sealed class FakeResolver : ITenantResolver { public string Name => "fake"; }

    [Fact]
    public async Task Production_boot_with_no_resolver_is_refused()
    {
        var act = async () => await TenancyRuntimeFixture.CreateAsync(environment: "Production");

        (await act.Should().ThrowAsync<TenancyBootException>())
            .Which.Failures.Should().Contain(f => f.Contains("no tenant resolver"));
    }

    [Fact]
    public async Task Production_boot_with_a_registered_resolver_succeeds()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(
            environment: "Production",
            configureServices: s => s.AddSingleton<ITenantResolver, FakeResolver>());

        runtime.Services.GetServices<ITenantResolver>().Should().ContainSingle();
    }

    [Fact]
    public async Task Production_boot_with_a_branded_dev_secret_is_refused()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Koan:Data:Tenancy:SigningSecret"] = TenancyDevBrand.Prefix + "leaked-into-prod",
        };

        var act = async () => await TenancyRuntimeFixture.CreateAsync(
            environment: "Production",
            extraSettings: settings,
            configureServices: s => s.AddSingleton<ITenantResolver, FakeResolver>());

        (await act.Should().ThrowAsync<TenancyBootException>())
            .Which.Failures.Should().Contain(f => f.Contains(TenancyDevBrand.Prefix));
    }

    [Fact]
    public async Task Non_production_boot_with_no_resolver_succeeds()
    {
        // The default Test environment is non-production → the pre-flight warns (logged) but never refuses.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();

        runtime.Services.Should().NotBeNull();
    }
}
