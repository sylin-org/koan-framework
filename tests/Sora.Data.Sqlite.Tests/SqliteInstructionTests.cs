using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Core;
using Xunit;
using DataServiceExecuteExtensions = Sora.Data.Relational.Extensions.DataServiceExecuteExtensions;

namespace Sora.Data.Sqlite.Tests;

public class SqliteInstructionTests
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;

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

        var newId = Guid.NewGuid().ToString("n");
        await DataServiceExecuteExtensions.Execute<Todo, bool>(data,
            "INSERT INTO Todo(Id, Title) VALUES(@id,@t)", new { id = newId, t = "hello" });

        var count = await data.Execute<Todo, long>(InstructionSql.Scalar("SELECT COUNT(*) FROM Todo"));
        count.Should().Be(1);
    }
}
