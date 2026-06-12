using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Filtering;

/// <summary>
/// SqlServer derivation of the comparable-encoding contract oracle (<see cref="TemporalConvergence"/>,
/// DATA-0100 / ARCH-0079). Proves DateTimeOffset (UTC-ISO nvarchar), TimeSpan (ticks, <c>CAST ... AS
/// BIGINT</c>), DateOnly/TimeOnly range comparisons converge with the compiled-predicate CLR oracle
/// through the real SqlServer adapter. Skips (vacuous pass) when no SQL Server is reachable.
/// </summary>
public class SqlServerComparableEncodingSpec : IClassFixture<Support.SqlServerAutoFixture>
{
    private readonly Support.SqlServerAutoFixture _fx;

    public SqlServerComparableEncodingSpec(Support.SqlServerAutoFixture fx) => _fx = fx;

    [Fact(DisplayName = "SqlServer: composite-scalar comparisons converge with the CLR oracle (DATA-0100)")]
    public async Task Composite_scalars_converge_with_oracle()
    {
        if (_fx.SkipTests) return;
        AppHost.Current = _fx.ServiceProvider;
        await TemporalConvergence.AssertConvergesAsync();
        await TemporalConvergence.AssertRoundTripAndOffsetStrippedAsync();
    }
}
