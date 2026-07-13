using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Regression: a <c>[Embedding]</c> entity deriving from the 1-arg <c>Entity&lt;T&gt;</c> must register
/// its embedding hooks at boot. <c>FindEntityBaseType</c> returns that 1-arg base, whose inherited static
/// <c>Events</c> property is only visible to reflection with <c>BindingFlags.FlattenHierarchy</c> — without
/// it, <c>AddKoan()</c> threw a <c>KoanBootException</c> ("does not have a static Events property") for
/// every <c>Entity&lt;T&gt;</c>-shaped <c>[Embedding]</c> type (surfaced by the S5.Recs Media dogfood, P2.1).
/// </summary>
[Collection(nameof(DataAiHostLifecycleCollection))]
public sealed class EmbeddingHookRegistrationSpec : IAsyncLifetime
{
    private IntegrationHost? _host;

    public async ValueTask InitializeAsync()
    {
        _host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .ConfigureServices(s => { s.AddLogging(); s.AddKoan(); })
            .StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public void Host_boots_with_an_Entity_T_shaped_embedding_entity()
    {
        // Reaching here means AddKoan() registered the embedding hook for EmbeddingHookEntity (below)
        // without throwing — the FlattenHierarchy fix in Koan.Data.AI's registrar.
        _host.Should().NotBeNull();
    }
}

/// <summary>A 1-arg <see cref="Entity{TEntity}"/> with <c>[Embedding]</c> — the shape that exposed the bug.</summary>
[Embedding]
public sealed class EmbeddingHookEntity : Entity<EmbeddingHookEntity>
{
    public string Text { get; set; } = "";
}
