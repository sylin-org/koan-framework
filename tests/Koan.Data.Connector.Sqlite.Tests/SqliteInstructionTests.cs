using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Testing;
using Xunit;
using DataServiceExecuteExtensions = Koan.Data.Relational.Extensions.DataServiceExecuteExtensions;

namespace Koan.Data.Connector.Sqlite.Tests;

public class SqliteInstructionTests : KoanTestBase
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;

    }

    private IServiceProvider BuildSqliteServices(string file)
    {
        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[] {
                    new KeyValuePair<string,string?>("SqliteOptions:ConnectionString", $"Data Source={file}"),
                    new KeyValuePair<string,string?>("Koan_DATA_PROVIDER","sqlite")
                })
                .Build();
            services.AddSingleton<IConfiguration>(cfg);
            services.AddSqliteAdapter(o => o.ConnectionString = $"Data Source={file}");
            services.AddKoanDataCore();
            services.AddSingleton<IDataService, DataService>();
        });
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Koan-sqlite-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    [Fact]
    public async Task EnsureCreated_And_Scalar_Works()
    {
        var file = TempFile();
        var sp = BuildSqliteServices(file);
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

