using Koan.Canon.Internal;

namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonRuntimeCommitSpec
{
    [Fact]
    public async Task Canonical_failure_attempts_no_index_or_audit_write()
    {
        var failure = new IOException("canonical unavailable");
        var persistence = new FailingPersistence(canonicalFailure: failure);
        var audit = new TrackingAuditSink();
        var runtime = Runtime(persistence, audit);

        var act = () => runtime.Canonize(new CommitCanon { Email = "canonical@example.com" });

        var error = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.Message.Should().Contain("checkpoint 'canonical'").And.Contain("no index or audit write");
        error.InnerException.Should().BeSameAs(failure);
        persistence.CanonicalAttempts.Should().Be(1);
        persistence.IndexAttempts.Should().Be(0);
        audit.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Index_failure_reports_durable_canonical_and_skips_audit()
    {
        var failure = new IOException("index unavailable");
        var persistence = new FailingPersistence(indexFailure: failure);
        var audit = new TrackingAuditSink();
        var runtime = Runtime(persistence, audit);

        var act = () => runtime.Canonize(new CommitCanon { Email = "index@example.com" });

        var error = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.Message.Should().Contain("checkpoint 'indexes'")
            .And.Contain("Canonical state is durable")
            .And.Contain("Do not assume rollback");
        error.InnerException.Should().BeSameAs(failure);
        persistence.CanonicalAttempts.Should().Be(1);
        persistence.IndexAttempts.Should().Be(1);
        audit.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Audit_failure_reports_durable_canonical_and_indexes()
    {
        var failure = new IOException("audit unavailable");
        var persistence = new FailingPersistence();
        var audit = new TrackingAuditSink(failure);
        var runtime = Runtime(persistence, audit);

        var act = () => runtime.Canonize(new CommitCanon { Email = "audit@example.com" });

        var error = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.Message.Should().Contain("checkpoint 'audit'")
            .And.Contain("Canonical state and aggregation indexes are durable")
            .And.Contain("blind-retry safety");
        error.InnerException.Should().BeSameAs(failure);
        persistence.CanonicalAttempts.Should().Be(1);
        persistence.IndexAttempts.Should().Be(1);
        audit.Attempts.Should().Be(1);
    }

    private static CanonRuntime Runtime(FailingPersistence persistence, TrackingAuditSink audit)
    {
        var builder = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .UseAuditSink(audit);
        builder.ConfigurePipeline<CommitCanon>(pipeline =>
            pipeline.AddStep(CanonPipelinePhase.Policy, (context, cancellationToken) =>
            {
                context.SetItem(DefaultPolicyContributor<CommitCanon>.AuditEntriesContextKey, new List<CanonAuditEntry>
                {
                    new() { Property = nameof(CommitCanon.Email), Policy = "test" }
                });
                return ValueTask.CompletedTask;
            }));
        return builder.Build();
    }

    [Canon(audit: true)]
    private sealed class CommitCanon : CanonEntity<CommitCanon>
    {
        [AggregationKey]
        public string Email { get; set; } = "";
    }

    private sealed class FailingPersistence(
        Exception? canonicalFailure = null,
        Exception? indexFailure = null) : ICanonPersistence
    {
        public int CanonicalAttempts { get; private set; }
        public int IndexAttempts { get; private set; }

        public Task<TModel?> GetCanonicalAsync<TModel>(string canonicalId, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult<TModel?>(null);

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            CanonicalAttempts++;
            return canonicalFailure is null
                ? Task.FromResult(entity)
                : Task.FromException<TModel>(canonicalFailure);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(stage);

        public Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken cancellationToken)
            => Task.FromResult<CanonIndex?>(null);

        public Task UpsertIndex(CanonIndex index, CancellationToken cancellationToken)
        {
            IndexAttempts++;
            return indexFailure is null ? Task.CompletedTask : Task.FromException(indexFailure);
        }
    }

    private sealed class TrackingAuditSink(Exception? failure = null) : ICanonAuditSink
    {
        public int Attempts { get; private set; }

        public Task Write(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
        {
            Attempts++;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }
}
