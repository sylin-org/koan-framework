using Koan.Data.Core.Model;
using Koan.Tests.Data.Core.Support;

namespace Koan.Tests.Data.Core.Specs.Routing;

public sealed class EntityPartitionRoutingSpec
{
    private readonly ITestOutputHelper _output;

    public EntityPartitionRoutingSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Json_provider_resolves_root_and_partition_sets()
    {
        await TestPipeline.For<EntityPartitionRoutingSpec>(_output, nameof(Json_provider_resolves_root_and_partition_sets))
            .Using<DataCoreRuntimeFixture>("runtime", static ctx => DataCoreRuntimeFixture.Create(ctx))
            .Arrange(static ctx =>
            {
                TestHooks.ResetDataConfigs();
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                runtime.ResetEntityCaches();
                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                runtime.BindHost();
                runtime.ResetEntityCaches();

                await Data<Todo, string>.DeleteAll();
                await Data<Todo, string>.DeleteAll("backup");

                var rootTitle = $"root-{Guid.CreateVersion7():n}";
                var backupTitle = $"backup-{Guid.CreateVersion7():n}";

                var rootEntity = new Todo { Title = rootTitle };
                await rootEntity.Save();

                (await Data<Todo, string>.All()).Should().ContainSingle(x => x.Title == rootTitle);

                await Data<Todo, string>.Upsert(new Todo { Title = backupTitle }, partition: "backup");
                (await Data<Todo, string>.All("backup")).Should().ContainSingle(x => x.Title == backupTitle);

                (await Data<Todo, string>.All()).Should().ContainSingle(x => x.Title == rootTitle);

                var removed = await Data<Todo, string>.Delete(x => x.Title == backupTitle, partition: "backup");
                removed.Should().Be(1);
                (await Data<Todo, string>.All("backup")).Should().BeEmpty();
            })
            .Run();
    }

    private sealed class Todo : Entity<Todo, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = "";
    }
}