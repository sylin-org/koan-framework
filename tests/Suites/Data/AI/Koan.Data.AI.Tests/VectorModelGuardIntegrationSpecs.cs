using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// AI-0036 W4 (hard throw) — live integration coverage per ARCH-0079. Drives the durable
/// <c>VectorModelRegistry</c> through a real <c>AddKoan()</c> + InMemory data host so the
/// registry persistence and the <c>GuardWrite</c>/<c>Reset</c> wiring actually round-trip through
/// the data layer (unit fakes structurally cannot prove that). The pure decision logic lives in
/// <see cref="VectorModelGuardSpecs"/>; this proves the full lifecycle end to end:
/// establish → same-model no-op → second-model throw → Reset (migration) → new model accepted.
/// </summary>
/// <remarks>
/// Uses a dedicated <see cref="W4GuardEntity"/> type so the guard's process-static confirmation
/// cache and the registry key (both keyed on <c>typeof(TEntity).Name</c>) are isolated from every
/// other spec. This is the only host-based class in the suite, so it never races other tests on
/// the process-global <c>AppHost.Current</c>.
/// </remarks>
public sealed class VectorModelGuardIntegrationSpecs : IAsyncLifetime
{
    private IntegrationHost? _host;

    public async Task InitializeAsync()
    {
        _host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", "memory://w4-guard")
            .ConfigureServices(s =>
            {
                s.AddLogging();
                s.AddKoan();
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    // Type marker only — the registry is VectorModelRegistry<W4GuardEntity>. A unique name keeps
    // this test's static cache + stored registry from colliding with anything else in the process.
    private sealed class W4GuardEntity { }

    [Fact]
    public async Task Registry_round_trips_the_full_model_lifecycle()
    {
        const string ModelA = "text-embedding-3-small";
        const string ModelB = "text-embedding-3-large";

        // 1. First write establishes the index's model.
        await VectorModelGuard.GuardWrite<W4GuardEntity>(ModelA);
        (await VectorModelGuard.ModelsInIndex<W4GuardEntity>()).Should().Equal(ModelA);

        // 2. The same model again is a no-op — the index stays single-model.
        await VectorModelGuard.GuardWrite<W4GuardEntity>(ModelA);
        (await VectorModelGuard.ModelsInIndex<W4GuardEntity>()).Should().Equal(ModelA);

        // 3. A different model into a single-model index THROWS at the boundary; the mixed-space
        //    write is prevented and the registry is left untouched.
        var mix = async () => await VectorModelGuard.GuardWrite<W4GuardEntity>(ModelB);
        var ex = (await mix.Should().ThrowAsync<VectorModelMismatchException>()).Which;
        ex.IndexModel.Should().Be(ModelA);
        ex.WriteModel.Should().Be(ModelB);
        (await VectorModelGuard.ModelsInIndex<W4GuardEntity>()).Should().Equal(ModelA);

        // 4. A by-design transition (what EmbeddingMigrator does) resets the registry to the target.
        await VectorModelGuard.Reset<W4GuardEntity>(ModelB);
        (await VectorModelGuard.ModelsInIndex<W4GuardEntity>()).Should().Equal(ModelB);

        // 5. After the reset the new model writes cleanly, and now the OLD model is the one that throws.
        await VectorModelGuard.GuardWrite<W4GuardEntity>(ModelB);
        (await VectorModelGuard.ModelsInIndex<W4GuardEntity>()).Should().Equal(ModelB);

        var reverse = async () => await VectorModelGuard.GuardWrite<W4GuardEntity>(ModelA);
        await reverse.Should().ThrowAsync<VectorModelMismatchException>();
    }
}
