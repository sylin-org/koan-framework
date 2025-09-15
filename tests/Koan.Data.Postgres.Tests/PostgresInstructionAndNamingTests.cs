using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Koan.Data.Postgres.Tests;

public class PostgresInstructionAndNamingTests : IClassFixture<PostgresAutoFixture>
{
    private readonly PostgresAutoFixture _fx;
    public PostgresInstructionAndNamingTests(PostgresAutoFixture fx) => _fx = fx;

    [Fact]
    public async Task Instructions_and_naming_basics()
    {
        if (_fx.SkipTests) return; // environment lacks Docker; treat as skipped
        var repo = _fx.Data.GetRepository<Doc, string>();

        var nsr = _fx.ServiceProvider.GetRequiredService<IStorageNameResolver>();
        var conv = new StorageNameResolver.Convention(StorageNamingStyle.FullNamespace, ".", NameCasing.AsIs);
        var logicalName = nsr.Resolve(typeof(Doc), conv);
        logicalName.Should().Contain("Doc");

        var exec = (IInstructionExecutor<Doc>)repo;
        var report = await exec.ExecuteAsync<object>(new Instruction("relational.schema.validate"), default);
        (report as IDictionary<string, object?>).Should().NotBeNull();

        await exec.ExecuteAsync<int>(new Instruction("relational.schema.clear"), default);

        await repo.UpsertManyAsync(new[]
        {
            new Doc("a") { Title = "A" }, new Doc("b") { Title = "B" }
        }, default);

        var all = await repo.QueryAsync(null, default);
        all.Count.Should().Be(2);

        var echoed = await exec.ExecuteAsync<int>(new Instruction("relational.sql.scalar", null, new Dictionary<string, object?> { ["Sql"] = $"SELECT COUNT(*) FROM Doc" }), default);
        echoed.Should().Be(2);
    }

    public sealed record Doc(string Id) : Abstractions.IEntity<string>
    {
        public string? Title { get; init; }
    }
}
