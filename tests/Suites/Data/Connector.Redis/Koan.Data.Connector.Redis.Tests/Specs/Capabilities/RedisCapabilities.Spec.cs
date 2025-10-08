using System;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Redis.Tests.Support;

namespace Koan.Data.Connector.Redis.Tests.Specs.Capabilities;

public sealed class RedisCapabilitiesSpec
{
    private readonly ITestOutputHelper _output;

    public RedisCapabilitiesSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Repository_reports_linq_and_fast_remove_capabilities()
    {
        await TestPipeline.For<RedisCapabilitiesSpec>(_output, nameof(Repository_reports_linq_and_fast_remove_capabilities))
            .RequireDocker()
            .UsingRedisContainer()
            .Using<RedisConnectorFixture>("fixture", static ctx => RedisConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<RedisConnectorFixture>("fixture");
                await fixture.ResetAsync<CapabilityProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<RedisConnectorFixture>("fixture");
                fixture.BindHost();

                var repository = fixture.Data.GetRepository<CapabilityProbe, string>();
                repository.Should().BeAssignableTo<ILinqQueryRepository<CapabilityProbe, string>>();
                repository.Should().BeAssignableTo<ILinqQueryRepositoryWithOptions<CapabilityProbe, string>>();
                repository.Should().BeAssignableTo<IDataRepositoryWithOptions<CapabilityProbe, string>>();

                var queryCaps = repository.Should().BeAssignableTo<IQueryCapabilities>().Subject;
                queryCaps.Capabilities.Should().Be(QueryCapabilities.Linq);

                var writeCaps = repository.Should().BeAssignableTo<IWriteCapabilities>().Subject;
                writeCaps.Writes.Should().Be(WriteCapabilities.FastRemove);

                await CapabilityProbe.UpsertAsync(new CapabilityProbe { Name = "cap" });
                var all = await CapabilityProbe.All();
                all.Should().ContainSingle(p => p.Name == "cap");
            })
            .RunAsync();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = string.Empty;
    }
}
