using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Sora.Data.Postgres.Tests;

public class PostgresCrudAndQueryTests : IClassFixture<PostgresAutoFixture>
{
    private readonly PostgresAutoFixture _fx;
    public PostgresCrudAndQueryTests(PostgresAutoFixture fx) => _fx = fx;

    [Fact]
    public async Task Upsert_read_query_count_and_paging()
    {
        if (_fx.SkipTests) return; // environment lacks Docker; treat as skipped
        var repo = _fx.Data.GetRepository<Person, string>();

        var people = Enumerable.Range(1, 25).Select(i => new Person(i.ToString())
        {
            Name = $"P-{i}",
            Age = 18 + (i % 5)
        }).ToArray();

        await repo.UpsertManyAsync(people, default);

        var all = await repo.QueryAsync(null, default);
        all.Count.Should().Be(25);

        var lrepo = (ILinqQueryRepository<Person, string>)repo;
        var page1 = await lrepo.QueryAsync(x => x.Age >= 20, default);
        page1.Count.Should().Be(5);
        page1.First().Name.Should().Be("P-2");

        var count = await lrepo.CountAsync(x => x.Age >= 20, default);
        count.Should().BeGreaterThan(0);
    }

    public sealed record Person(string Id) : Sora.Data.Abstractions.IEntity<string>
    {
        public string? Name { get; init; }
        public int Age { get; init; }
    }
}
