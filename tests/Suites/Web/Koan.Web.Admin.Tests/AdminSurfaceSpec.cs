using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Web.Admin.Tests;

public sealed class AdminSurfaceSpec
{
    [Fact]
    public async Task PackageReferenceAndAddKoanMountTheAuthenticatedDashboard()
    {
        await using var host = await AdminWebHost.StartAsync();
        Authenticate(host.Client);

        var response = await host.Client.GetAsync("/.koan/admin/", TestContext.Current.CancellationToken);
        var health = await host.Client.GetAsync("/.koan/admin/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Runtime truth, without a second control plane.");
    }

    [Fact]
    public async Task StatusMasksSecretsAndNeverProjectsHostIdentity()
    {
        await using var host = await AdminWebHost.StartAsync();
        Authenticate(host.Client);

        var json = await host.Client.GetStringAsync("/.koan/admin/status", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        json.Should().NotContain("never-project-this");
        json.Should().Contain("********");
        root.GetProperty("runtime").GetProperty("sanitized").GetBoolean().Should().BeTrue();
        root.GetProperty("runtime").GetProperty("locked").GetBoolean().Should().BeTrue();
        IsAbsentOrNull(root.GetProperty("runtime").GetProperty("process"), "userName").Should().BeTrue();
        IsAbsentOrNull(root.GetProperty("runtime").GetProperty("process"), "commandLine").Should().BeTrue();
        IsAbsentOrNull(root.GetProperty("runtime").GetProperty("machine"), "machineName").Should().BeTrue();
        IsAbsentOrNull(root.GetProperty("runtime").GetProperty("machine"), "domainName").Should().BeTrue();
    }

    [Fact]
    public async Task AnonymousAndInsufficientUsersUseStandardPolicyResults()
    {
        await using var host = await AdminWebHost.StartAsync(
            settings: new Dictionary<string, string?>
            {
                ["Koan:Admin:Authorization:AutoCreateDevelopmentPolicy"] = "false"
            },
            configureAuthorization: options => options.AddPolicy("KoanAdmin", policy => policy.RequireRole("admin")));

        var anonymous = await host.Client.GetAsync("/.koan/admin/status", TestContext.Current.CancellationToken);
        Authenticate(host.Client);
        var forbidden = await host.Client.GetAsync("/.koan/admin/status", TestContext.Current.CancellationToken);
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
        var allowed = await host.Client.GetAsync("/.koan/admin/status", TestContext.Current.CancellationToken);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Development", "false")]
    public async Task OutsideTheDevelopmentBoundaryAdminIsNotDiscoverable(string environment, string? enabled)
    {
        var settings = enabled is null
            ? null
            : new Dictionary<string, string?> { ["Koan:Admin:Enabled"] = enabled };
        await using var host = await AdminWebHost.StartAsync(environment, settings);
        Authenticate(host.Client);

        var response = await host.Client.GetAsync("/.koan/admin/status", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StandardConfigurationMovesTheEntireSurfaceAsOneUnit()
    {
        await using var host = await AdminWebHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Koan:Admin:PathPrefix"] = "ops"
        });
        Authenticate(host.Client);

        var moved = await host.Client.GetAsync("/ops/admin/status", TestContext.Current.CancellationToken);
        var old = await host.Client.GetAsync("/.koan/admin/status", TestContext.Current.CancellationToken);

        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        old.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/.koan/admin/api/manifest")]
    [InlineData("/.koan/admin/api/launchkit")]
    [InlineData("/.koan/admin/api/service-mesh")]
    [InlineData("/.koan/admin/api/styles")]
    public async Task RetiredControlPlaneEndpointsStayGone(string route)
    {
        await using var host = await AdminWebHost.StartAsync();
        Authenticate(host.Client);

        var response = await host.Client.GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Koan:Admin:PathPrefix", "not/a/prefix", "*may only contain letters, digits, '.', '-', or '_' characters*")]
    [InlineData("Koan:Admin:Authorization:Policy", " ", "*cannot be blank*")]
    public async Task InvalidOptionsFailStartupWithTheCorrection(string key, string value, string correction)
    {
        Func<Task> start = async () =>
        {
            await using var _ = await AdminWebHost.StartAsync(settings: new Dictionary<string, string?>
            {
                [key] = value
            });
        };

        var failure = await Record.ExceptionAsync(start);
        failure.Should().NotBeNull();

        var validationFailure = failure switch
        {
            OptionsValidationException validation => validation,
            AggregateException aggregate => aggregate.Flatten().InnerExceptions
                .OfType<OptionsValidationException>()
                .Single(),
            _ => throw new Xunit.Sdk.XunitException($"Expected an options-validation failure, but found {failure!.GetType().FullName}.")
        };

        validationFailure.Message.Should().Match(correction);
    }

    [Fact]
    public async Task Overlapping_hosts_preserve_the_newer_owner_when_the_older_host_stops()
    {
        var older = await AdminWebHost.StartAsync();
        var newer = await AdminWebHost.StartAsync();
        try
        {
            AppHost.Current.Should().BeSameAs(newer.AmbientServices);
            await older.DisposeAsync();
            AppHost.Current.Should().BeSameAs(newer.AmbientServices);
        }
        finally
        {
            await newer.DisposeAsync();
            await older.DisposeAsync();
        }

        AppHost.Current.Should().BeNull();
    }

    private static void Authenticate(HttpClient client)
        => client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", "developer");

    private static bool IsAbsentOrNull(JsonElement parent, string propertyName)
        => !parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null;
}
