using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// Boot-smoke for the in-process vector floor (per ARCH-0079). Proves the InMemory vector connector is
/// discovered through real <c>AddKoan()</c> reflective bootstrap (Reference = Intent) — no explicit
/// <c>AddKoanDataVector()</c> — is elected for an entity when the data provider is in-memory, and performs a
/// real k-NN round-trip ranked by <c>System.Numerics.Tensors</c>. This is the shipping adapter that also
/// backs the vector-matrix convergence oracle, so a green here means the single-binary semantic-search
/// story holds end-to-end with zero infrastructure.
/// </summary>
public sealed class VectorInMemoryPillarBootstrapSpec
{
    private const string VecBootSpace = "vec-boot";

    public sealed class VecBootDoc : Entity<VecBootDoc> { }

    [Fact]
    public async Task AddKoan_discovers_inmemory_vector_adapter_and_ranks_by_cosine()
    {
        await using var host = await PillarHost.Configure()
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Environment", "Test")
            // The space declaration is schema, not registration: dimension and metric cannot be inferred from
            // an entity, exactly as a relational entity's column widths cannot. The claim under test is that
            // the adapter is DISCOVERED and ELECTED without AddKoanDataVector(), which still holds.
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("Default").Vector<VecBootDoc>(space => space.Name(VecBootSpace).Dimensions(3))))
            .StartAsync();

        // Reference = Intent: with Koan.Data.Vector + the InMemory vector connector referenced,
        // IVectorService resolves and elects the in-memory factory (it CanHandle the "inmemory" data
        // provider name) without any explicit registration call.
        var vectors = host.Services.GetRequiredService<IVectorService>();
        var repo = vectors.TryGetRepository<VecBootDoc, string>();
        repo.Should().NotBeNull("the InMemory vector connector must be discovered via Reference = Intent");

        // The legacy Upsert overload is a NotSupportedException default on the current contract; points go
        // through Save(VectorPoint, VectorScope).
        await repo!.Save(new VectorPoint<string>("east", new[] { 1f, 0f, 0f }, null), VectorScope.Unscoped);
        await repo.Save(new VectorPoint<string>("north", new[] { 0f, 1f, 0f }, null), VectorScope.Unscoped);

        // A query leaning east must rank "east" first — proves the TensorPrimitives cosine path runs.
        var result = await repo.Search(
            new VectorSearchRequest(new[] { 0.9f, 0.1f, 0f }, Top: 2, Space: VecBootSpace), VectorScope.Unscoped);
        result.Items.Should().HaveCount(2);
        ((string)(object)result.Items[0].Id).Should().Be("east");
    }
}
