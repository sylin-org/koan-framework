using System;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.InMemory.Tests.Support;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Crud;

public sealed class InMemoryCrudSpec
{
    private readonly ITestOutputHelper _output;

    public InMemoryCrudSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Upsert_query_count_and_remove_flow()
    {
        await TestPipeline.For<InMemoryCrudSpec>(_output, nameof(Upsert_query_count_and_remove_flow))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<Person, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);

                await using var lease = fixture.LeasePartition(partition);

                var saved = await Person.UpsertAsync(new Person { Name = "Ada", Age = 34 });
                var originalTimestamp = saved.LastUpdated;
                saved.Id.Should().NotBeNullOrWhiteSpace();

                await Person.UpsertMany(new[]
                {
                    new Person { Name = "Grace", Age = 47 },
                    new Person { Name = "Bob", Age = 42 }
                });

                var all = await Person.All(partition);
                all.Should().HaveCount(3);

                var filtered = await Person.Query(p => p.Age > 40);
                filtered.Should().HaveCount(2);

                var updated = filtered.First();
                updated.Name = "Bobby";
                await Person.UpsertAsync(updated);

                var fetched = await Person.Get(updated.Id);
                fetched!.Name.Should().Be("Bobby");
                fetched.LastUpdated.Should().NotBe(originalTimestamp);

                var count = await Person.Count.Where(p => p.Age >= 40, CountStrategy.Exact);
                count.Should().Be(2);

                var removed = await Person.Remove(saved.Id, new DataQueryOptions { Partition = partition });
                removed.Should().BeTrue();

                var remaining = await Person.All(partition);
                remaining.Should().HaveCount(2);
            })
            .RunAsync();
    }

    private sealed class Person : Entity<Person>
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        [Timestamp]
        public DateTimeOffset LastUpdated { get; set; }
    }
}
