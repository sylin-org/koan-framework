using System;
using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Connector.InMemory.Tests.Support;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Filtering;

/// <summary>
/// InMemory derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100). The in-memory adapter compares live CLR objects (no serialization), so it is the floor
/// the contract is measured against — this dockerless spec confirms the range comparisons on
/// DateTimeOffset/TimeSpan/DateOnly/TimeOnly flow through the LINQ pipeline without throwing and match
/// the compiled-predicate oracle. (The offset-stripping round-trip assertion does NOT apply here: the
/// in-memory store keeps the original CLR value, offset intact.)
/// </summary>
public sealed class InMemoryComparableEncodingSpec
{
    private readonly ITestOutputHelper _output;
    public InMemoryComparableEncodingSpec(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "InMemory: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        await TestPipeline
            .For<InMemoryComparableEncodingSpec>(_output, nameof(Composite_scalars_converge_with_oracle))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.Create(ctx))
            .Assert(async ctx =>
            {
                ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture").BindHost();
                await TemporalConvergence.AssertConvergesAsync();
            })
            .Run();
    }
}
