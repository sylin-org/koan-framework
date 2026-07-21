using System.Collections.Generic;
using System.Threading.Tasks;
using Testcontainers.MsSql;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 SQL Server container fixture. Starts the official <see cref="MsSqlContainer"/> module
/// (accepts the EULA + sets a strong SA password; <c>GetConnectionString()</c> includes
/// <c>TrustServerCertificate=True</c> and targets <c>master</c>) and hands it to the Koan data layer.
/// The 2022 image is heavy (~15-30s first start). The old fixture's <c>DefaultPageSize=5</c> override is
/// dropped — the only paging spec passes <c>PageSize=5</c> explicitly in its query, so nothing depends on it.
/// </summary>
public sealed class SqlServerFixture : KoanContainerFixture
{
    private MsSqlContainer? _container;

    public override string Engine => "sqlserver";
    protected override string Adapter => "sqlserver";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU6-GDR1-ubuntu-24.04").Build();
        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) => new[]
    {
        new KeyValuePair<string, string?>("Koan:Data:SqlServer:ConnectionString", connectionString),
    };
}
