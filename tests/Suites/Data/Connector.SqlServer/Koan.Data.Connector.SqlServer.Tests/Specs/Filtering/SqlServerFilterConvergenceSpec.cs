using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Filtering;

/// <summary>
/// SqlServer derivation of the shared filter-convergence oracle (<see cref="FilterConvergence"/>,
/// ARCH-0079). Runs every filter through the real SqlServer adapter and the in-memory floor and asserts
/// identical id-sets. Skips (vacuous pass) when no SQL Server is reachable (LocalDB / Docker), matching
/// the other SqlServer specs.
/// </summary>
public class SqlServerFilterConvergenceSpec : IClassFixture<Support.SqlServerAutoFixture>
{
    private readonly Support.SqlServerAutoFixture _fx;

    public SqlServerFilterConvergenceSpec(Support.SqlServerAutoFixture fx) => _fx = fx;

    [Fact(DisplayName = "SqlServer: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        if (_fx.SkipTests) return;
        AppHost.Current = _fx.ServiceProvider;
        await FilterConvergence.AssertConvergesAsync();
    }
}
