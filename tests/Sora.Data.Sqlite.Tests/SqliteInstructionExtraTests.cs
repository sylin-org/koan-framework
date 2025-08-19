using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Core;
using Sora.Data.Sqlite;
using Xunit;

namespace Sora.Data.Sqlite.Tests;

public class SqliteInstructionExtraTests
{
    public class Todo : Sora.Data.Abstractions.IEntity<string>
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildServices(string file)
    {
        var sc = new ServiceCollection();
    var cs = $"Data Source={file}";
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("SqliteOptions:ConnectionString", cs),
        new KeyValuePair<string,string?>("SORA_DATA_PROVIDER","sqlite"),
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => o.ConnectionString = cs);
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
    public async Task DataInstructions_EnsureCreated_And_Clear()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var ensured = await data.Execute<Todo, bool>(new Instruction("data.ensureCreated"));
        ensured.Should().BeTrue();

        await repo.UpsertAsync(new Todo { Title = "x" });
        (await repo.CountAsync((object?)null)).Should().BeGreaterThan(0);

        var cleared = await data.Execute<Todo, int>(new Instruction("data.clear"));
        cleared.Should().BeGreaterOrEqualTo(1);
        (await repo.CountAsync((object?)null)).Should().Be(0);
    }
}
