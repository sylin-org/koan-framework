using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>ARCH-0142: a host with authentication, identity and their sweeps referenced changes no schema after it reports started, and its OAuth and session tables are there.</summary>
public sealed class SchemaSettledSpec
{
    [Fact]
    public async Task Nothing_changes_the_schema_after_the_host_reports_started()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-verbs-settled-{Guid.CreateVersion7():n}.db");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            AppHost.Current = null;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False",
                ["Koan:Mcp:EnableStdioTransport"] = "false",
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddScoped<TenantHolder>();
            builder.Services.AddAuthorization(options => options.AddPolicy(Forum.NobodyPasses, policy => policy.RequireAssertion(_ => false)));
            builder.Services.AddKoan();
            await using var app = builder.Build();
            await app.StartAsync(ct);

            var atStart = Version(path);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            Version(path).Should().Be(atStart);
            await app.StopAsync(ct);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    private static long Version(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA schema_version";
        return (long)command.ExecuteScalar()!;
    }
}
