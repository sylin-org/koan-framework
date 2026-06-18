using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Koan.Web.OpenApi.Tests;

/// <summary>
/// (C20a follow-up) Pins the <c>Koan:OpenApi:EnableUi</c> gate introduced when
/// <c>Koan.Web.Connector.Swagger</c> merged into <c>Koan.Web.OpenApi</c> — previously guarded only by
/// review, with no test net. <c>EnableUi</c> is read FIRST and overrides both the legacy
/// <c>Koan:Web:Swagger:Enabled</c> toggle and the environment default; when no <c>EnableUi</c> is set the
/// gate falls through to the legacy toggle / Reference=Intent default. Verified through a real
/// <c>WebApplicationFactory</c> host: the Swagger UI is served at <c>/swagger/index.html</c> iff the gate is on.
/// </summary>
public sealed class SwaggerEnableUiGateSpec : IClassFixture<SwaggerWebApplicationFactory>
{
    private const string SwaggerUi = "/swagger/index.html";
    private readonly SwaggerWebApplicationFactory _factory;

    public SwaggerEnableUiGateSpec(SwaggerWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Swagger_ui_is_served_by_default_outside_production()
    {
        // Reference=Intent: referencing the Swagger module enables the UI by default outside Production.
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync(SwaggerUi);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EnableUi_false_disables_the_swagger_ui()
    {
        using var client = WithSetting(("Koan:OpenApi:EnableUi", "false")).CreateClient();
        var resp = await client.GetAsync(SwaggerUi);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnableUi_true_enables_the_swagger_ui()
    {
        using var client = WithSetting(("Koan:OpenApi:EnableUi", "true")).CreateClient();
        var resp = await client.GetAsync(SwaggerUi);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EnableUi_true_overrides_legacy_swagger_enabled_false()
    {
        // Legacy says off, EnableUi says on — EnableUi is read first and wins.
        using var client = WithSetting(
            ("Koan:Web:Swagger:Enabled", "false"),
            ("Koan:OpenApi:EnableUi", "true")).CreateClient();
        var resp = await client.GetAsync(SwaggerUi);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Legacy_swagger_enabled_false_disables_when_EnableUi_unset()
    {
        // With no EnableUi, the legacy toggle still gates the UI (fall-through preserved).
        using var client = WithSetting(("Koan:Web:Swagger:Enabled", "false")).CreateClient();
        var resp = await client.GetAsync(SwaggerUi);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private WebApplicationFactory<Program> WithSetting(params (string Key, string Value)[] settings)
        => _factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings)
            {
                b.UseSetting(key, value);
            }
        });
}
