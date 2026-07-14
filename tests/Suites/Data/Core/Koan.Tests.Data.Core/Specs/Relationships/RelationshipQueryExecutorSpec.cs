using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Tests.Data.Core.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Relationships;

[Collection(nameof(RelationshipQueryExecutorSpec))]
[CollectionDefinition(nameof(RelationshipQueryExecutorSpec), DisableParallelization = true)]
public sealed class RelationshipQueryExecutorSpec : IAsyncLifetime
{
    private DataCoreRuntimeFixture _fixture = null!;

    public async ValueTask InitializeAsync()
        => _fixture = await DataCoreRuntimeFixture.CreateAsync(includeSqlite: true);

    public async ValueTask DisposeAsync()
        => await _fixture.DisposeAsync();

    [Fact]
    public async Task InMemory_selects_in_memory_execution_and_records_the_decision()
    {
        _fixture.BindHost();
        var parent = new MemoryParent { Name = "parent" };
        await parent.Save();
        await new MemoryChild { ParentId = parent.Id, Name = "included" }.Save();
        await new MemoryChild { ParentId = Guid.CreateVersion7().ToString(), Name = "other" }.Save();

        var result = await Executor.LoadChildren<MemoryParent, MemoryChild, string>(
            [parent.Id], nameof(MemoryChild.ParentId));

        result.Decision.Mode.Should().Be(RelationshipExecutionMode.InMemory);
        result.ByParent[parent.Id].Select(child => child.Name).Should().Equal("included");
        Facts.Current.Facts.Should().Contain(fact =>
            fact.Code == Koan.Data.Core.Infrastructure.Constants.Diagnostics.Codes.RelationshipExecution
            && fact.State == KoanFactState.Selected
            && fact.ReasonCode == Koan.Data.Core.Infrastructure.Constants.Diagnostics.Reasons.InMemoryFilter);
    }

    [Fact]
    public async Task Sqlite_selects_native_execution()
    {
        _fixture.BindHost();
        var parent = new SqliteParent();
        await parent.Save();
        await new SqliteChild { ParentId = parent.Id, Name = "native" }.Save();

        var result = await Executor.LoadChildren<SqliteParent, SqliteChild, string>(
            [parent.Id], nameof(SqliteChild.ParentId));

        result.Decision.Mode.Should().Be(RelationshipExecutionMode.Native);
        result.ByParent[parent.Id].Should().ContainSingle().Which.Name.Should().Be("native");
    }

    [Fact]
    public async Task Entity_first_child_surface_uses_the_same_negotiation_policy()
    {
        _fixture.BindHost();
        var memoryParent = new MemoryParent();
        await memoryParent.Save();
        await new MemoryChild { ParentId = memoryParent.Id, Name = "entity-first" }.Save();

        var memoryChildren = await memoryParent.GetChildren<MemoryChild>();
        memoryChildren.Should().ContainSingle().Which.Name.Should().Be("entity-first");

        var jsonParent = new JsonParent();
        await jsonParent.Save();
        await new JsonChild { ParentId = jsonParent.Id, Name = "explicit" }.Save();

        await Assert.ThrowsAsync<RelationshipQueryRejectedException>(() =>
            jsonParent.GetChildren<JsonChild>());
        var jsonChildren = await jsonParent.GetChildren<JsonChild>(RelationshipQueryPolicy.Bounded(10));
        jsonChildren.Should().ContainSingle().Which.Name.Should().Be("explicit");
    }

    [Fact]
    public async Task Json_rejects_an_implicit_scan_but_accepts_an_explicit_bounded_scan()
    {
        _fixture.BindHost();
        var parent = new JsonParent();
        await parent.Save();
        await new JsonChild { ParentId = parent.Id, Name = "bounded" }.Save();

        var rejected = await Assert.ThrowsAsync<RelationshipQueryRejectedException>(() =>
            Executor.LoadChildren<JsonParent, JsonChild, string>([parent.Id], nameof(JsonChild.ParentId)));
        rejected.ReasonCode.Should().Be(Koan.Data.Core.Infrastructure.Constants.Diagnostics.Reasons.UnboundedScan);

        var result = await Executor.LoadChildren<JsonParent, JsonChild, string>(
            [parent.Id],
            nameof(JsonChild.ParentId),
            policy: RelationshipQueryPolicy.Bounded(10));

        result.Decision.Mode.Should().Be(RelationshipExecutionMode.BoundedScan);
        result.Decision.CandidatesExamined.Should().Be(1);
        result.ByParent[parent.Id].Should().ContainSingle().Which.Name.Should().Be("bounded");
    }

    [Fact]
    public async Task Bounded_scan_refuses_partial_results_and_honors_cancellation()
    {
        _fixture.BindHost();
        var parent = new LimitedJsonParent();
        await parent.Save();
        await new LimitedJsonChild { ParentId = parent.Id }.Save();
        await new LimitedJsonChild { ParentId = parent.Id }.Save();
        await new LimitedJsonChild { ParentId = parent.Id }.Save();

        var rejected = await Assert.ThrowsAsync<RelationshipQueryRejectedException>(() =>
            Executor.LoadChildren<LimitedJsonParent, LimitedJsonChild, string>(
                [parent.Id],
                nameof(LimitedJsonChild.ParentId),
                policy: RelationshipQueryPolicy.Bounded(2)));
        rejected.ReasonCode.Should().Be(Koan.Data.Core.Infrastructure.Constants.Diagnostics.Reasons.FallbackLimit);
        rejected.IsLimitExceeded.Should().BeTrue();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Executor.LoadChildren<LimitedJsonParent, LimitedJsonChild, string>(
                [parent.Id],
                nameof(LimitedJsonChild.ParentId),
                policy: RelationshipQueryPolicy.Bounded(10),
                ct: cancelled.Token));
    }

    private IRelationshipQueryExecutor Executor
        => _fixture.Services.GetRequiredService<IRelationshipQueryExecutor>();

    private IKoanRuntimeFacts Facts
        => _fixture.Services.GetRequiredService<IKoanRuntimeFacts>();

    [DataAdapter("inmemory")]
    private sealed class MemoryParent : Entity<MemoryParent> { public string? Name { get; set; } }

    [DataAdapter("inmemory")]
    private sealed class MemoryChild : Entity<MemoryChild>
    {
        [Parent(typeof(MemoryParent))]
        public string ParentId { get; set; } = "";
        public string? Name { get; set; }
    }

    [DataAdapter("sqlite")]
    private sealed class SqliteParent : Entity<SqliteParent>;

    [DataAdapter("sqlite")]
    private sealed class SqliteChild : Entity<SqliteChild>
    {
        [Parent(typeof(SqliteParent))]
        public string ParentId { get; set; } = "";
        public string? Name { get; set; }
    }

    [DataAdapter("json")]
    private sealed class JsonParent : Entity<JsonParent>;

    [DataAdapter("json")]
    private sealed class JsonChild : Entity<JsonChild>
    {
        [Parent(typeof(JsonParent))]
        public string ParentId { get; set; } = "";
        public string? Name { get; set; }
    }

    [DataAdapter("json")]
    private sealed class LimitedJsonParent : Entity<LimitedJsonParent>;

    [DataAdapter("json")]
    private sealed class LimitedJsonChild : Entity<LimitedJsonChild>
    {
        [Parent(typeof(LimitedJsonParent))]
        public string ParentId { get; set; } = "";
    }
}
