using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Core;
using Xunit;

namespace Sora.Data.Sqlite.Tests;

public class SqliteInstructionTests
{
    public class Todo : Sora.Data.Abstractions.IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;

        // Test-local sugar: allow Todo.Execute(sql, ...) defaulting to NonQuery (no return)
        public static async System.Threading.Tasks.Task Execute(string sql, IDataService data, object? parameters = null, System.Threading.CancellationToken ct = default)
            => await Data<Todo>.Execute(sql, data, parameters, ct);
    }

    private static IServiceProvider BuildServices(string file)
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("SqliteOptions:ConnectionString", $"Data Source={file}"),
                new KeyValuePair<string,string?>("SORA_DATA_PROVIDER","sqlite")
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => o.ConnectionString = $"Data Source={file}");
        sc.AddSoraDataCore();
        sc.AddSingleton<IDataService, DataService>();
        return sc.BuildServiceProvider();
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sora-sqlite-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    [Fact]
    public async Task EnsureCreated_And_Scalar_Works()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();

        var ensured = await data.Execute<Todo, bool>(new Instruction("relational.schema.ensureCreated"));
        ensured.Should().BeTrue();

        await Todo.Execute("INSERT INTO Todo(Id, Title) VALUES(@id,@t)", data, new { id = "1", t = "x" });

        var count = await data.Execute<Todo, long>(InstructionSql.Scalar("SELECT COUNT(*) FROM Todo"));
        count.Should().Be(1);
    }
}
