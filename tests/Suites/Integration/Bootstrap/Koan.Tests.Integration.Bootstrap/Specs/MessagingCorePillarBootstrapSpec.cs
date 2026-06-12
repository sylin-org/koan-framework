using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Messaging;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Messaging pillar core (per ARCH-0079). Proves the messaging proxy
/// surface resolves through real <c>AddKoan()</c> reflective discovery.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="IMessageProxy"/> and not <c>IMessageBus</c>?</b> Discovery: this boot
/// smoke initially asserted <c>IMessageBus</c> resolution and failed because the bus is
/// produced lazily by an <c>IMessagingProvider.CreateBus</c> call which requires a connector
/// (RabbitMQ, etc.). <c>IMessageProxy</c> is what <c>AddKoanMessaging</c> actually registers
/// as the user-facing surface — the <c>.Send()</c> / <c>.On&lt;T&gt;()</c> entry point. A
/// broker-backed <c>IMessageBus</c> boot smoke belongs in the
/// <c>Koan.Messaging.Connector.RabbitMq</c> adapter suite.
/// </para>
/// <para>
/// See <see cref="DataCorePillarBootstrapSpec"/> for the residual cross-pillar Redis config
/// note — the data connector's eager-connect is a separate concern from ARCH-0080.
/// </para>
/// </remarks>
public sealed class MessagingCorePillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public MessagingCorePillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IMessageProxy_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var proxy = host.Services.GetRequiredService<IMessageProxy>();
        proxy.Should().NotBeNull();
    }
}
