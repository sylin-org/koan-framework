using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace S1.Web.IntegrationTests;

public sealed class S1SmokeTests
{

    [Fact]
    public async Task health_seed_list_flow_should_work()
    {
        // Reset config cache to avoid contamination from previous factories
        Sora.Data.Core.TestHooks.ResetDataConfigs();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("urls", "http://localhost:0");
            builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
            var jsonDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s1-web-it-json");
            System.IO.Directory.CreateDirectory(jsonDir);
            var sqliteFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s1-web-it-sqlite.db");
            builder.UseSetting("Sora:Data:Sources:Default:json:DirectoryPath", jsonDir);
            builder.UseSetting("Sora:Data:Sources:Default:sqlite:ConnectionString", $"Data Source={sqliteFile}");
        });
        var client = app.CreateClient();

        // health
        using var healthDoc = await client.GetFromJsonAsync<System.Text.Json.JsonDocument>("/api/health");
        healthDoc!.RootElement.GetProperty("status").GetString().Should().Be("ok");

        // clear just in case
        await client.DeleteAsync("/api/todo/clear");

        // seed
        var resp = await client.PostAsync("/api/todo/seed/5", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // list
        var listResp = await client.GetAsync("/api/todo?page=1&size=10");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await listResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        body.GetArrayLength().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task health_endpoints_should_return_expected_status_codes()
    {
        Sora.Data.Core.TestHooks.ResetDataConfigs();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("urls", "http://localhost:0");
            builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
            var jsonDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s1-web-it-json");
            System.IO.Directory.CreateDirectory(jsonDir);
            var sqliteFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s1-web-it-sqlite.db");
            builder.UseSetting("Sora:Data:Sources:Default:json:DirectoryPath", jsonDir);
            builder.UseSetting("Sora:Data:Sources:Default:sqlite:ConnectionString", $"Data Source={sqliteFile}");
        });
        var client = app.CreateClient();

        var live = await client.GetAsync("/health/live");
        live.StatusCode.Should().Be(HttpStatusCode.OK);

        var ready = await client.GetAsync("/health/ready");
        // In normal dev config with writable local paths, readiness should be OK.
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task readiness_should_be_unhealthy_when_json_path_invalid()
    {
        Sora.Data.Core.TestHooks.ResetDataConfigs();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("urls", "http://localhost:0");
            builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
            // Valid sqlite so only JSON drives readiness unhealthy
            var sqliteFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s1-web-it-sqlite.db");
            builder.UseSetting("Sora:Data:Sources:Default:sqlite:ConnectionString", $"Data Source={sqliteFile}");
            // Force JSON adapter directory to a path that cannot exist or be created (invalid drive letter)
            builder.UseSetting("Sora:Data:Sources:Default:json:DirectoryPath", "Z:|\\invalid?path");
        });

        var client = app.CreateClient();
        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task readiness_should_be_unhealthy_when_sqlite_connection_invalid()
    {
        Sora.Data.Core.TestHooks.ResetDataConfigs();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("urls", "http://localhost:0");
            builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
            // Valid JSON so only SQLite drives readiness unhealthy
            var jsonDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s1-web-it-json");
            System.IO.Directory.CreateDirectory(jsonDir);
            builder.UseSetting("Sora:Data:Sources:Default:json:DirectoryPath", jsonDir);
            // For SQLite, override via default named source pattern used by our configurator
            builder.UseSetting("Sora:Data:Sources:Default:sqlite:ConnectionString", "Data Source=Z:|\\invalid?path\\bad.sqlite");
        });

        var client = app.CreateClient();
        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
