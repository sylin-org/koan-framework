using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Testing;
using Xunit;

namespace Koan.Data.Connector.Sqlite.Tests;

public class SqliteInstructionExtraTests : KoanTestBase
{
    public class Todo : Abstractions.IEntity<string>
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private IServiceProvider BuildSqliteServices(string file)
    {
        var cs = $"Data Source={file}";
        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[] {
                    new KeyValuePair<string,string?>("SqliteOptions:ConnectionString", cs),
                    new KeyValuePair<string,string?>("Koan_DATA_PROVIDER","sqlite"),
                })
                .Build();
            services.AddSingleton<IConfiguration>(cfg);
            services.AddSqliteAdapter(o => o.ConnectionString = cs);
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
    public async Task DataInstructions_EnsureCreated_And_Clear()
    {
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var ensured = await data.Execute<Todo, bool>(new Instruction("data.ensureCreated"));
        ensured.Should().BeTrue();

        await repo.UpsertAsync(new Todo { Title = "x" });
        var countResult1 = await repo.CountAsync(new CountRequest<Todo>());
        countResult1.Value.Should().BeGreaterThan(0);

        var cleared = await data.Execute<Todo, int>(new Instruction("data.clear"));
        cleared.Should().BeGreaterThanOrEqualTo(1);
        var countResult2 = await repo.CountAsync(new CountRequest<Todo>());
        countResult2.Value.Should().Be(0);
    }
}

