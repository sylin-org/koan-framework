using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.WebSockets;
using Koan.WebSockets.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.WebSockets.Tests;

public class ServiceCollectionExtensionsTests
{
    private const string ConfigurationSection = "Koan:Web:WebSockets";

    [Fact]
    public async Task AddWebSocketStreamAdapters_BindsOptionsAndRegistersFactory()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [$"{ConfigurationSection}:MessageType"] = WebSocketMessageType.Text.ToString(),
            [$"{ConfigurationSection}:LeaveOpen"] = bool.TrueString,
            [$"{ConfigurationSection}:SubProtocol"] = "proto.v1"
        });

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddWebSocketStreamAdapters();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WebSocketStreamOptions>>().Value;

        options.MessageType.Should().Be(WebSocketMessageType.Text);
        options.LeaveOpen.Should().BeTrue();
        options.SubProtocol.Should().Be("proto.v1");

        var factory = provider.GetRequiredService<IWebSocketStreamFactory>();
        var socket = new TestWebSocket();
        using (var stream = factory.CreateWritable(socket))
        {
            var payload = new byte[] { 10 };
            await stream.WriteAsync(payload, 0, payload.Length, CancellationToken.None);
        }

        socket.LastSendMessageType.Should().Be(WebSocketMessageType.Text);
    }

    [Fact]
    public void AddWebSocketStreamAdapters_WhenConfigurationMissing_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddWebSocketStreamAdapters();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WebSocketStreamOptions>>().Value;

        options.MessageType.Should().Be(WebSocketMessageType.Binary);
        options.LeaveOpen.Should().BeFalse();
        options.SubProtocol.Should().BeNull();
    }

    [Fact]
    public async Task CreateWritable_UsesDefaultMessageTypeWhenOverrideNotSpecified()
    {
        var services = new ServiceCollection();
        services.AddWebSocketStreamAdapters();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IWebSocketStreamFactory>();
        var socket = new TestWebSocket();

        using (var stream = factory.CreateWritable(socket))
        {
            var payload = new byte[] { 1 };
            await stream.WriteAsync(payload, 0, payload.Length, CancellationToken.None);
        }

        socket.LastSendMessageType.Should().Be(WebSocketMessageType.Binary);
    }

    [Fact]
    public async Task CreateWritable_HonorsOverrideMessageType()
    {
        var services = new ServiceCollection();
        services.AddWebSocketStreamAdapters();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IWebSocketStreamFactory>();
        var socket = new TestWebSocket();

        using (var stream = factory.CreateWritable(socket, WebSocketMessageType.Text))
        {
            var payload = new byte[] { 2 };
            await stream.WriteAsync(payload, 0, payload.Length, CancellationToken.None);
        }

        socket.LastSendMessageType.Should().Be(WebSocketMessageType.Text);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
