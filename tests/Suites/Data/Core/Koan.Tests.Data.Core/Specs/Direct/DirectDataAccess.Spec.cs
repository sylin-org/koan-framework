using Koan.Data.Core.Direct;
using Koan.Tests.Data.Core.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Direct;

/// <summary>
/// Guards the ARCH-0090 §1 fold: the Direct data-access implementation moved out of the former
/// standalone <c>Koan.Data.Direct</c> package into <c>Koan.Data.Core</c> and is now registered by
/// default. These specs prove <see cref="IDirectDataService"/> resolves out-of-box (no
/// <c>AddKoanDataDirect()</c>) and executes real SQL through the auto-registered SQLite factory.
/// </summary>
public sealed class DirectDataAccessSpec
{
    private readonly ITestOutputHelper _output;

    public DirectDataAccessSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Direct_is_registered_by_default_and_executes_sql()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(includeSqlite: true);

        // The fold's behavioral delta: Direct is wired by default via AddKoanDataCore() —
        // no separate AddKoanDataDirect() package/registration is required.
        runtime.Services.GetService<IDirectDataService>().Should().NotBeNull();

        var data = runtime.Services.GetRequiredService<IDataService>();
        var session = data.Direct(adapter: "sqlite")
            .WithConnectionString($"Data Source={runtime.SqlitePath}");

        await session.Execute("CREATE TABLE IF NOT EXISTS direct_probe (id INTEGER PRIMARY KEY, name TEXT)");
        var inserted = await session.Execute(
            "INSERT INTO direct_probe (name) VALUES (@name)",
            new { name = "folded" });
        inserted.Should().Be(1);

        var count = await session.Scalar<long>("SELECT COUNT(*) FROM direct_probe");
        count.Should().Be(1);

        var rows = await session.Query<ProbeRow>("SELECT id, name FROM direct_probe");
        rows.Should().ContainSingle();
        rows[0].Name.Should().Be("folded");
    }

    [Fact]
    public async Task Direct_transaction_commits_and_rolls_back()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(includeSqlite: true);

        var data = runtime.Services.GetRequiredService<IDataService>();
        var connectionString = $"Data Source={runtime.SqlitePath}";

        var setup = data.Direct(adapter: "sqlite").WithConnectionString(connectionString);
        await setup.Execute("CREATE TABLE IF NOT EXISTS direct_tx (id INTEGER PRIMARY KEY, name TEXT)");

        // Committed work is durable.
        await using (var tx = data.Direct(adapter: "sqlite").WithConnectionString(connectionString).Begin())
        {
            await tx.Execute("INSERT INTO direct_tx (name) VALUES (@name)", new { name = "committed" });
            await tx.Commit();
        }

        // Rolled-back work is discarded.
        await using (var tx = data.Direct(adapter: "sqlite").WithConnectionString(connectionString).Begin())
        {
            await tx.Execute("INSERT INTO direct_tx (name) VALUES (@name)", new { name = "discarded" });
            await tx.Rollback();
        }

        var count = await setup.Scalar<long>("SELECT COUNT(*) FROM direct_tx");
        count.Should().Be(1);

        var rows = await setup.Query<ProbeRow>("SELECT id, name FROM direct_tx");
        rows.Should().ContainSingle();
        rows[0].Name.Should().Be("committed");
    }

    private sealed class ProbeRow
    {
        public long Id { get; set; }
        public string? Name { get; set; }
    }
}
