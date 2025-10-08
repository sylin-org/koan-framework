using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Json.Tests.Support;

namespace Koan.Data.Connector.Json.Tests.Specs.Crud;

public sealed class JsonCrudSpec
{
    private readonly ITestOutputHelper _output;

    public JsonCrudSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Upsert_query_count_and_remove_flow()
    {
        await TestPipeline.For<JsonCrudSpec>(_output, nameof(Upsert_query_count_and_remove_flow))
            .Using<JsonConnectorFixture>("fixture", static ctx => JsonConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                await fixture.ResetAsync<Person, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                fixture.BindHost();

                var partitionRoot = fixture.EnsurePartition(ctx);
                var partition = $"{partitionRoot}-crud";

                await using var lease = fixture.LeasePartition(partition);

                var saved = await Person.UpsertAsync(new Person { Name = "Ada", Age = 34 });
                saved.Id.Should().NotBeNullOrWhiteSpace();
                var originalTimestamp = saved.LastUpdated;

                await Person.UpsertMany(new[]
                {
                    new Person { Name = "Grace", Age = 47 },
                    new Person { Name = "Bob", Age = 42 },
                    new Person { Name = "Edsger", Age = 59 }
                });

                var all = await Person.All(partition);
                all.Should().HaveCount(4);

                var filtered = await Data<Person, string>.Query(p => p.Age >= 40, partition);
                filtered.Should().HaveCount(3);

                var updated = filtered.First(p => p.Name == "Bob");
                updated.Name = "Bobby";
                updated.Age = 43;
                await Person.UpsertAsync(updated);

                var fetched = await Person.Get(updated.Id);
                fetched.Should().NotBeNull();
                fetched!.Name.Should().Be("Bobby");
                fetched.LastUpdated.Should().BeOnOrAfter(originalTimestamp);

                var count = await Data<Person, string>.CountAsync(p => p.Age >= 40, partition);
                count.Should().Be(3);

                var page = await Person.Page(1, 2);
                page.Should().HaveCount(2);

                var removed = await Person.Remove(saved.Id, new DataQueryOptions { Partition = partition });
                removed.Should().BeTrue();

                var remainingIds = filtered.Select(p => p.Id).Skip(1).ToArray();
                var removedMany = await Person.Remove(remainingIds);
                removedMany.Should().Be(2);

                var remaining = await Person.All(partition);
                remaining.Should().HaveCount(1);
                remaining[0].Name.Should().Be("Grace");
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
