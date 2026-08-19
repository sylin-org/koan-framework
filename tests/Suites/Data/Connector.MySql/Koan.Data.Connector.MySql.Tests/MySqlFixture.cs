using Testcontainers.MySql;

namespace Koan.Data.Connector.MySql.Tests;

public sealed class MySqlFixture : KoanContainerFixture
{
    private const string Image = "mysql:8.4@sha256:b3b90af2a6552ae30c266fdb7d5dd55f3afb72404bb78d37fe8a23eb857fd3fb";
    private const string Database = "koan";
    private const string User = "root";
    private const string Password = "koan";
    private const string RootHost = "%";

    private MySqlContainer? _container;

    public override string Engine => "mysql";
    protected override string Adapter => "mysql";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new MySqlBuilder(Image)
            .WithDatabase(Database)
            .WithUsername(User)
            .WithPassword(Password)
            .WithEnvironment("MYSQL_ROOT_HOST", RootHost)
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) =>
    [
        new("Koan:Data:MySql:ConnectionString", connectionString),
        new("Koan:Data:MySql:Readiness:EnableReadinessGating", "false"),
    ];
}
