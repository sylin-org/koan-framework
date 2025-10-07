using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Mongo.Tests.Support;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Crud;

public sealed class MongoCrudSpec
{
    private readonly ITestOutputHelper _output;

    public MongoCrudSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Upsert_query_count_and_remove_flow()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline.For<MongoCrudSpec>(_output, nameof(Upsert_query_count_and_remove_flow))
            .RequireDocker()
            .UsingMongoContainer(database: databaseName)
            .Using<MongoConnectorFixture>("fixture", static ctx => MongoConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<MongoConnectorFixture>("fixture");
                await fixture.ResetAsync<Person, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<MongoConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);

                await using var lease = fixture.LeasePartition(partition);

                var saved = await Person.UpsertAsync(new Person { Name = "Ada", Age = 34 });
                saved.Id.Should().NotBeNullOrWhiteSpace();
                var originalTimestamp = saved.LastUpdated;

                await Person.UpsertMany(new[]
                {
                    new Person { Name = "Grace", Age = 47 },
                    new Person { Name = "Bob", Age = 42 }
                });

                var all = await Person.All(partition);
                all.Should().HaveCount(3);

                var filtered = await Data<Person, string>.Query(p => p.Name != "Ada", partition);
                filtered.Should().HaveCount(2);

                var updated = filtered.First();
                updated.Name = "Bobby";
                await Person.UpsertAsync(updated);

                var fetched = await Person.Get(updated.Id);
                fetched.Should().NotBeNull();
                fetched!.Name.Should().Be("Bobby");
                fetched.LastUpdated.Should().NotBe(originalTimestamp);

                var count = await Data<Person, string>.CountAsync(p => p.Name != "Ada", partition);
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
