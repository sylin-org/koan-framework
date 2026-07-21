using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.AI.Web.Tests;

public sealed class AiWebProjectionSpec
{
    [Fact]
    public async Task Package_reference_and_AddKoan_expose_an_inspectable_inactive_projection()
    {
        AppHost.Current = null;
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Test");
                web.ConfigureServices(services => services.AddKoan());
                web.Configure(_ => { });
            })
            .StartAsync(TestContext.Current.CancellationToken);

        using var client = host.GetTestClient();
        using var healthResponse = await client.GetAsync("/ai/health", TestContext.Current.CancellationToken);
        using var capabilityResponse = await client.GetAsync("/ai/capabilities", TestContext.Current.CancellationToken);

        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var health = await healthResponse.Content.ReadFromJsonAsync<HealthEnvelope>(TestContext.Current.CancellationToken);
        health.Should().NotBeNull();
        health!.State.Should().Be("Inactive");
        health.Adapters.Should().Be(0);
        health.Message.Should().Contain("no provider adapter");

        capabilityResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var capabilities = await capabilityResponse.Content.ReadFromJsonAsync<object[]>(TestContext.Current.CancellationToken);
        capabilities.Should().BeEmpty();

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed record HealthEnvelope(string State, int Adapters, string Message);
}
