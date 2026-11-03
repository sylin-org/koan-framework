using Koan.Core;
using Koan.Mcp.Options;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Mcp.Conformance.Tests;

public sealed class TransportConfigurationSpec
{
    [Fact]
    public async Task Explicit_current_transport_options_remain_effective()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:BackgroundServices:Enabled", "false")
            .WithSetting("Koan:Mcp:EnableStdioTransport", "false")
            .WithSetting("Koan:Mcp:EnableStreamableHttpTransport", "true")
            .ConfigureServices(services => services.AddKoan()).StartAsync();
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Assert.True(options.EnableStreamableHttpTransport);
    }
}
