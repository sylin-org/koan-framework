using System;
using System.IO;
using System.Linq;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Json.Tests.Support;

namespace Koan.Data.Connector.Json.Tests.Specs.Partition;

public sealed class JsonPartitionSpec
{
    private readonly ITestOutputHelper _output;

    public JsonPartitionSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Partition_scopes_isolate_entities()
    {
        await TestPipeline.For<JsonPartitionSpec>(_output, nameof(Partition_scopes_isolate_entities))
            .Using<JsonConnectorFixture>("fixture", static ctx => JsonConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                await fixture.ResetAsync<TenantRecord, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                fixture.BindHost();

                var partitionRoot = fixture.EnsurePartition(ctx);
                var partitionA = $"{partitionRoot}-a";
                var partitionB = $"{partitionRoot}-b";

                await using (fixture.LeasePartition(partitionA))
                {
                    await TenantRecord.UpsertAsync(new TenantRecord { Name = "A1" });
                    await TenantRecord.UpsertAsync(new TenantRecord { Name = "A2" });
                }

                await using (fixture.LeasePartition(partitionB))
                {
                    await TenantRecord.UpsertAsync(new TenantRecord { Name = "B1" });
                }

                var defaultScope = await TenantRecord.All();
                defaultScope.Should().BeEmpty();

                var partitionAResults = await TenantRecord.All(partitionA);
                partitionAResults.Should().HaveCount(2);
                partitionAResults.Select(e => e.Name).Should().BeEquivalentTo(new[] { "A1", "A2" });

                var partitionBResults = await TenantRecord.All(partitionB);
                partitionBResults.Should().HaveCount(1);
                partitionBResults[0].Name.Should().Be("B1");

                await using (fixture.LeasePartition(partitionA))
                {
                    var removed = await TenantRecord.Remove(partitionAResults.Select(r => r.Id));
                    removed.Should().Be(2);
                }

                var afterADelete = await TenantRecord.All(partitionA);
                afterADelete.Should().BeEmpty();

                var afterBDelete = await TenantRecord.All(partitionB);
                afterBDelete.Should().ContainSingle();

                var jsonFiles = Directory.Exists(fixture.RootPath)
                    ? Directory.EnumerateFiles(fixture.RootPath, "*.json", SearchOption.AllDirectories).ToArray()
                    : Array.Empty<string>();

                jsonFiles.Should().NotBeEmpty("each partition should materialize its own json store");
            })
            .RunAsync();
    }

    private sealed class TenantRecord : Entity<TenantRecord>
    {
        public string Name { get; set; } = string.Empty;
    }
}
