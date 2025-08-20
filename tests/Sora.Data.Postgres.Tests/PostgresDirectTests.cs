using FluentAssertions;
using Sora.Data.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Sora.Data.Postgres.Tests;

public class PostgresDirectTests : IClassFixture<PostgresAutoFixture>
{
    private readonly PostgresAutoFixture _fx;
    public PostgresDirectTests(PostgresAutoFixture fx) => _fx = fx;

    [Fact]
    public async Task Direct_scalar_and_query_via_instruction_executor()
    {
        if (_fx.SkipTests) return; // environment lacks Docker; treat as skipped

        var repo = _fx.Data.GetRepository<DirectPerson, string>();
        var people = Enumerable.Range(1, 10).Select(i => new DirectPerson(i.ToString()) { Name = $"P-{i}", Age = 18 + (i % 5) }).ToArray();
        await repo.UpsertManyAsync(people, default);

        // Scalar via executor path (source uses entity:Type)
        var data = _fx.Data;
        var count = await data.Direct("entity:Sora.Data.Postgres.Tests.DirectPerson")
            .Scalar<long>("select count(*) from DirectPerson where (\"Json\" #>> '{Age}')::int >= @age", new { age = 20 });
        count.Should().BeGreaterThan(0);

        // Query via executor path
        var rows = await data.Direct("entity:Sora.Data.Postgres.Tests.DirectPerson")
            .Query("select \"Id\", \"Json\" from DirectPerson order by \"Id\" limit 3");
        rows.Count.Should().Be(3);
        var first = (System.Collections.Generic.IDictionary<string, object?>)rows[0]!;
        first.ContainsKey("Id").Should().BeTrue();
    }

    public sealed record DirectPerson(string Id) : IEntity<string>
    {
        public string? Name { get; init; }
        public int Age { get; init; }
    }
}
