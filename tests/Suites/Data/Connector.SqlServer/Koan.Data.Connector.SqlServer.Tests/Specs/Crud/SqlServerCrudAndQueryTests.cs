using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Crud;

public class SqlServerCrudAndQueryTests : IClassFixture<Support.SqlServerAutoFixture>
{
	private readonly Support.SqlServerAutoFixture _fx;

	public SqlServerCrudAndQueryTests(Support.SqlServerAutoFixture fx) => _fx = fx;

	[Fact]
	public async Task Upsert_query_count_and_paging()
	{
		using var partition = BeginPartition("crud-core");
		var (available, repo, queryRepo) = await Prepare();
		if (!available)
		{
			return;
		}

		var people = Enumerable.Range(1, 25)
			.Select(i => new Person
			{
				Id = i.ToString(CultureInfo.InvariantCulture),
				Name = $"P-{i}",
				Age = 18 + (i % 5)
			})
			.ToArray();

		await repo.UpsertMany(people, default);

		// Querying + counting live on IQueryRepository<T,K> via QueryDefinition; LINQ predicates lower
		// into the unified Filter AST through LinqFilterCompiler (the entity-first DX path).
		var age20 = LinqFilterCompiler.Compile<Person>(x => x.Age >= 20);

		var all = (await queryRepo.Query(QueryDefinition.All, default)).Items;
		all.Should().HaveCount(25);

		var page = (await queryRepo.Query(new QueryDefinition { Filter = age20, Page = 1, PageSize = 5 }, default)).Items;
		page.Should().HaveCount(5);
		page.First().Name.Should().Be("P-2");

		var countResult = await queryRepo.Count(new QueryDefinition { Filter = age20 });
		countResult.IsEstimate.Should().BeFalse();
		countResult.Value.Should().Be(15);

		var fetched = await repo.Get("10", default);
		fetched.Should().NotBeNull();

		var updated = new Person
		{
			Id = "10",
			Name = "Updated-10",
			Age = (fetched!.Age) + 5
		};

		await repo.Upsert(updated, default);
		(await repo.Get("10", default))!.Name.Should().Be("Updated-10");

		var countAfterUpdate = await queryRepo.Count(new QueryDefinition { Filter = age20 });
		countAfterUpdate.IsEstimate.Should().BeFalse();
		countAfterUpdate.Value.Should().Be(16);

		var removed = await repo.Delete("1", default);
		removed.Should().BeTrue();
		(await repo.Get("1", default)).Should().BeNull();

		var removedMany = await repo.DeleteMany(new[] { "2", "3" }, default);
		removedMany.Should().Be(2);

		var remaining = (await queryRepo.Query(QueryDefinition.All, default)).Items;
		remaining.Should().HaveCount(22);

		var finalCount = await queryRepo.Count(new QueryDefinition { Filter = age20 });
		finalCount.Value.Should().Be(14);
		finalCount.IsEstimate.Should().BeFalse();
	}

	private async Task<(bool Available, IDataRepository<Person, string> Repo, IQueryRepository<Person, string> QueryRepo)> Prepare()
	{
		if (_fx.SkipTests)
		{
			return (false, default!, default!);
		}

		EnsureAppHost();
		await _fx.Data.Execute<Person, int>(new Instruction("data.clear"));

		var repo = _fx.Data.GetRepository<Person, string>();
		var queryRepo = Assert.IsAssignableFrom<IQueryRepository<Person, string>>(repo);
		return (true, repo, queryRepo);
	}

	private void EnsureAppHost()
	{
		if (!ReferenceEquals(AppHost.Current, _fx.ServiceProvider))
		{
			AppHost.Current = _fx.ServiceProvider;
		}
	}

	private static IDisposable BeginPartition(string prefix)
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		return EntityContext.Partition($"sql-{prefix}-{token}");
	}

	public class Person : Entity<Person>
	{
		public string Name { get; set; } = "";
		public int Age { get; set; }
	}
}
