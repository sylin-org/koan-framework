using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Filtering;

/// <summary>
/// InMemory derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100). The in-memory adapter compares live CLR objects (no serialization), so it is the floor
/// the contract is measured against — this dockerless spec confirms the range comparisons on
/// DateTimeOffset/TimeSpan/DateOnly/TimeOnly flow through the LINQ pipeline without throwing and match
/// the compiled-predicate oracle. (The offset-stripping round-trip assertion does NOT apply here: the
/// in-memory store keeps the original CLR value, offset intact.)
/// </summary>
public sealed class InMemoryComparableEncodingSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact(DisplayName = "InMemory: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await TemporalConvergence.AssertConvergesAsync();
    }
}
