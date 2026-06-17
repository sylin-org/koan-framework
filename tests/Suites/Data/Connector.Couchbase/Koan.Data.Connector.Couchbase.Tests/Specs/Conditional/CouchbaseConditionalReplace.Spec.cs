using System;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Couchbase.Tests.Support;

namespace Koan.Data.Connector.Couchbase.Tests.Specs.Conditional;

/// <summary>
/// (DATA-0102) Couchbase native CAS. <c>ConditionalReplaceAsync</c> applies the replace iff the guard
/// holds on the document's pre-update state, and the adapter self-reports <c>Write.ConditionalReplace</c>.
/// This is the JOBS-0005 §20.3 contention-free claim pattern: claim a "queued" job by CAS-ing it to
/// "running"; a second claimant whose guard is now stale must lose (no write).
/// </summary>
public sealed class CouchbaseConditionalReplaceSpec
{
    private readonly ITestOutputHelper _output;

    public CouchbaseConditionalReplaceSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConditionalReplace_applies_on_guard_and_is_noop_when_stale()
    {
        await TestPipeline.For<CouchbaseConditionalReplaceSpec>(_output, nameof(ConditionalReplace_applies_on_guard_and_is_noop_when_stale))
            .RequireDocker()
            .UsingCouchbaseContainer()
            .Using<CouchbaseConnectorFixture>("fixture", static ctx => CouchbaseConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<CouchbaseConnectorFixture>("fixture");
                await fixture.ResetAsync<CasJob, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<CouchbaseConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                var repo = fixture.Data.GetRepository<CasJob, string>();

                // ARCH-0084: Couchbase self-reports native CAS.
                DataCaps.Describe(repo, repo.GetType().Name).Has(DataCaps.Write.ConditionalReplace).Should().BeTrue();

                var cas = repo as IConditionalWriteRepository<CasJob, string>;
                cas.Should().NotBeNull("Couchbase declares Write.ConditionalReplace, so the repository must expose the CAS contract");

                var saved = await CasJob.Upsert(new CasJob { Status = "queued", Owner = "" });

                // (a) Guard holds (status == queued) -> claim applied.
                var claim1 = new CasJob { Id = saved.Id, Status = "running", Owner = "node-1" };
                (await cas!.ConditionalReplaceAsync(claim1, j => j.Status == "queued")).Should().BeTrue();
                (await CasJob.All(partition)).Single().Owner.Should().Be("node-1");

                // (b) Guard is now stale (status == running, not queued) -> no-op, store unchanged.
                var claim2 = new CasJob { Id = saved.Id, Status = "running", Owner = "node-2" };
                (await cas!.ConditionalReplaceAsync(claim2, j => j.Status == "queued")).Should().BeFalse();
                (await CasJob.All(partition)).Single().Owner.Should().Be("node-1"); // node-2 lost the race
            })
            .Run();
    }
}

internal sealed class CasJob : Entity<CasJob>
{
    public string Status { get; set; } = "";
    public string Owner { get; set; } = "";
}
