using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Crud;

public sealed class SqlServerCrudAndQueryTests(SqlServerFixture fixture, ITestOutputHelper output)
	: KoanDataSpec<SqlServerFixture>(fixture, output)
{
	[Fact]
	public async Task Upsert_query_count_and_paging()
	{
		RequireBackingStore();
		await using var host = await BootAsync();
		var data = host.Services.GetRequiredService<IDataService>();
		var partition = NewPartition("crud-core");
		using var lease = Lease(partition);

		var repo = data.GetRepository<Person, string>();
		var queryRepo = Assert.IsAssignableFrom<IQueryRepository<Person, string>>(repo);

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

	public class Person : Entity<Person>
	{
		public string Name { get; set; } = "";
		public int Age { get; set; }
	}
}
