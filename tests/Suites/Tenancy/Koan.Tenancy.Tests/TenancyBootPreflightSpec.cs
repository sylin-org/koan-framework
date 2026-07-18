using AwesomeAssertions;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Tenancy.Tests;

public sealed class TenancyBootPreflightSpec
{
    [Fact]
    public async Task Closed_headless_production_host_does_not_require_an_http_resolver()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(environment: "Production");
        runtime.Services.GetService(typeof(TenancyRuntime)).Should().NotBeNull();
    }

    [Fact]
    public async Task Production_boot_with_a_config_forced_open_posture_is_refused()
    {
        var settings = new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Open" };
        var act = async () => await TenancyRuntimeFixture.CreateAsync(
            environment: "Production",
            extraSettings: settings);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("outside Development");
    }

    [Fact]
    public async Task Production_boot_with_a_programmatic_open_override_is_refused()
    {
        var act = async () => await TenancyRuntimeFixture.CreateAsync(
            environment: "Production",
            configureServices: services => services.Configure<TenancyOptions>(
                options => options.Posture = TenancyPosture.Open));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("outside Development");
    }
}
