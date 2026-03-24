using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
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
		var (available, repo, linqRepo) = await Prepare();
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

		var all = await repo.Query(null, default);
		all.Should().HaveCount(25);

		var page = await linqRepo.Query(x => x.Age >= 20, default);
		page.Should().HaveCount(5);
		page.First().Name.Should().Be("P-2");

		var countResult = await repo.Count(new CountRequest<Person>
		{
			Predicate = x => x.Age >= 20
		});

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

		var countAfterUpdate = await repo.Count(new CountRequest<Person>
		{
			Predicate = x => x.Age >= 20
		});

	countAfterUpdate.IsEstimate.Should().BeFalse();
	countAfterUpdate.Value.Should().Be(16);

		var removed = await repo.Delete("1", default);
		removed.Should().BeTrue();
		(await repo.Get("1", default)).Should().BeNull();

		var removedMany = await repo.DeleteMany(new[] { "2", "3" }, default);
		removedMany.Should().Be(2);

		var remaining = await repo.Query(null, default);
		remaining.Should().HaveCount(22);

		var finalCount = await repo.Count(new CountRequest<Person>
		{
			Predicate = x => x.Age >= 20
		});

		finalCount.Value.Should().Be(14);
		finalCount.IsEstimate.Should().BeFalse();
	}

	private async Task<(bool Available, IDataRepository<Person, string> Repo, ILinqQueryRepository<Person, string> LinqRepo)> Prepare()
	{
		if (_fx.SkipTests)
		{
			return (false, default!, default!);
		}

		EnsureAppHost();
		await _fx.Data.Execute<Person, int>(new Instruction("data.clear"));

		var repo = _fx.Data.GetRepository<Person, string>();
		var linqRepo = Assert.IsAssignableFrom<ILinqQueryRepository<Person, string>>(repo);
		return (true, repo, linqRepo);
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
		public string Name { get; set; } = string.Empty;
		public int Age { get; set; }
	}
}
