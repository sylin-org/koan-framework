using Koan.Data.Core.Model;
using Koan.Tests.Data.Core.Support;
using System.IO;

namespace Koan.Tests.Data.Core.Specs.Routing;

public sealed class CrossProviderDataMovementSpec
{
    private readonly ITestOutputHelper _output;

    public CrossProviderDataMovementSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Adapter_switches_route_between_json_and_sqlite()
    {
        await TestPipeline.For<CrossProviderDataMovementSpec>(_output, nameof(Adapter_switches_route_between_json_and_sqlite))
            .Using<DataCoreRuntimeFixture>("runtime", static ctx => DataCoreRuntimeFixture.CreateAsync(ctx, includeSqlite: true))
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

                await Data<TestEntity, string>.DeleteAllAsync();
                using (EntityContext.Adapter("json"))
                {
                    await Data<TestEntity, string>.DeleteAllAsync();
                }

                using (EntityContext.Adapter("sqlite"))
                {
                    await Data<TestEntity, string>.DeleteAllAsync();
                }

                var title = $"Cross Provider Test {Guid.CreateVersion7():n}";
                var entity = new TestEntity { Title = title, Value = 42 };

                using (EntityContext.Adapter("json"))
                {
                    var saved = await Data<TestEntity, string>.UpsertAsync(entity);
                    saved.Id.Should().NotBeNullOrWhiteSpace();
                }

                using (EntityContext.Adapter("sqlite"))
                {
                    var saved = await Data<TestEntity, string>.UpsertAsync(entity);
                    saved.Id.Should().NotBeNullOrWhiteSpace();
                }

                using (EntityContext.Adapter("json"))
                {
                    var jsonData = await Data<TestEntity, string>.All();
                    jsonData.Should().ContainSingle(x => x.Title == title);
                }

                using (EntityContext.Adapter("sqlite"))
                {
                    var sqliteData = await Data<TestEntity, string>.All();
                    sqliteData.Should().ContainSingle(x => x.Title == title);
                }

                Directory.EnumerateFiles(runtime.RootPath, "*.json", SearchOption.AllDirectories)
                    .Should().NotBeEmpty();

                runtime.SqlitePath.Should().NotBeNull();
                File.Exists(runtime.SqlitePath!).Should().BeTrue();
            })
            .RunAsync();
    }

    private sealed class TestEntity : Entity<TestEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}