using System.Net;
using AwesomeAssertions;
using Xunit;

namespace Koan.Web.OpenApi.Tests;

public sealed class OpenApiUiPolicySpec : IClassFixture<OpenApiWebApplicationFactory>
{
    private const string OpenApiDocument = "/openapi/v1.json";
    private const string OpenApiUi = "/swagger/index.html";
    private readonly OpenApiWebApplicationFactory _factory;

    public OpenApiUiPolicySpec(OpenApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ui_is_available_by_default_in_development()
    {
        using var client = _factory.CreateClientForEnvironment("Development");

        var response = await client.GetAsync(OpenApiUi, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ui_is_not_published_by_default_outside_development()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(OpenApiUi, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Explicit_ui_disable_wins_in_development()
    {
        using var client = _factory.CreateClientForEnvironment(
            "Development",
            ("Koan:OpenApi:EnableUi", "false"));

        var response = await client.GetAsync(OpenApiUi, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ui_enabled_outside_development_fails_closed_without_authentication()
    {
        using var client = _factory.CreateClient(("Koan:OpenApi:EnableUi", "true"));

        var response = await client.GetAsync(OpenApiUi, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("requires authentication");
    }

    [Fact]
    public async Task Application_can_deliberately_publish_an_open_ui_outside_development()
    {
        using var client = _factory.CreateClient(
            ("Koan:OpenApi:EnableUi", "true"),
            ("Koan:OpenApi:RequireAuthenticationOutsideDevelopment", "false"));

        var response = await client.GetAsync(OpenApiUi, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disabling_the_document_also_disables_the_ui()
    {
        using var client = _factory.CreateClientForEnvironment(
            "Development",
            ("Koan:OpenApi:Enabled", "false"),
            ("Koan:OpenApi:EnableUi", "true"));

        var document = await client.GetAsync(OpenApiDocument, TestContext.Current.CancellationToken);
        var ui = await client.GetAsync(OpenApiUi, TestContext.Current.CancellationToken);

        document.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ui.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ui_route_and_document_route_resolve_from_one_option_family()
    {
        using var client = _factory.CreateClientForEnvironment(
            "Development",
            ("Koan:OpenApi:UiRoute", "docs"),
            ("Koan:OpenApi:RoutePattern", "/contracts/{documentName}.json"));

        var document = await client.GetAsync("/contracts/v1.json", TestContext.Current.CancellationToken);
        var ui = await client.GetAsync("/docs/index.html", TestContext.Current.CancellationToken);
        var bootstrap = await client.GetAsync("/docs/index.js", TestContext.Current.CancellationToken);

        document.StatusCode.Should().Be(HttpStatusCode.OK);
        ui.StatusCode.Should().Be(HttpStatusCode.OK);
        bootstrap.StatusCode.Should().Be(HttpStatusCode.OK);
        (await bootstrap.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("/contracts/v1.json");
    }
}
