using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Redis.IntegrationTests;

public sealed class RedisCrudAndQueryTests : IClassFixture<RedisAutoFixture>
{
    private readonly RedisAutoFixture _fx;

    public RedisCrudAndQueryTests(RedisAutoFixture fx) => _fx = fx;

    private ServiceProvider CreateProvider()
    {
        var sc = new ServiceCollection();
        var cfgBuilder = new ConfigurationBuilder();
        // Env override if provided by fixture
        if (!string.IsNullOrWhiteSpace(_fx.ConnectionString))
        {
            // Prefer the primary config key used by adapter
            cfgBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Koan:Data:Redis:ConnectionString"] = _fx.ConnectionString,
            });
        }
        var cfg = cfgBuilder.AddEnvironmentVariables().Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddKoan();

        return sc.BuildServiceProvider();
    }

    private sealed class Person : IEntity<string>
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public int Age { get; set; }
    }

    [Fact]
    public async Task Upsert_get_query_count_and_delete_should_work()
    {
        if (string.IsNullOrWhiteSpace(_fx.ConnectionString)) return; // no Docker/env: skip
        using var sp = CreateProvider();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Person, string>();

        await repo.DeleteAllAsync();

        await repo.UpsertAsync(new Person { Id = "1", Name = "Ada", Age = 37 });
        await repo.UpsertAsync(new Person { Id = "2", Name = "Bob", Age = 17 });

        var one = await repo.GetAsync("1");
        one!.Name.Should().Be("Ada");

        var linq = (ILinqQueryRepository<Person, string>)repo;
        var adults = await linq.QueryAsync(p => p.Age >= 18);
        adults.Select(a => a.Id).Should().BeEquivalentTo(new[] { "1" });

        var total = await linq.CountAsync(p => p.Age >= 0);
        total.Should().Be(2);

        var deleted = await repo.DeleteAsync("1");
        deleted.Should().BeTrue();

        var left = await linq.CountAsync(p => p.Age >= 0);
        left.Should().Be(1);
    }

    [Fact]
    public async Task Paging_guardrails_should_apply()
    {
        if (string.IsNullOrWhiteSpace(_fx.ConnectionString)) return; // no Docker/env: skip
        using var sp = CreateProvider();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Person, string>();
        await repo.DeleteAllAsync();

        // create 5
        for (var i = 1; i <= 5; i++)
        {
            await repo.UpsertAsync(new Person { Id = i.ToString(), Name = $"N{i}", Age = 20 + i });
        }

        var q = await repo.QueryAsync(null);
        q.Count.Should().BeGreaterThan(0); // default page size is positive

        var opts = new DataQueryOptions { Page = 1, PageSize = 2 };
        var page = await ((IDataRepositoryWithOptions<Person, string>)repo).QueryAsync(null, opts);
        page.Should().HaveCount(2);
    }
}
