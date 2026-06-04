using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Core.Tests.Support;
using Koan.Jobs.Execution;
using Koan.Jobs.Model;
using Koan.Jobs.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Jobs.Core.Tests.Specs.Execution;

/// <summary>
/// Behaviour spec for the orphan-reaper recovery (lease lapse + revert to Queued). Exercises
/// <see cref="JobTypeRegistry.ReapOrphansAll"/> directly against the in-memory data adapter so
/// we test the actual SaveSelf + Enqueue flow without the polling loop in <c>JobOrphanReaper</c>.
/// </summary>
public sealed class JobOrphanReaperSpec
{
    [Fact(DisplayName = "Reaper: revives Running rows whose lease has lapsed")]
    public async Task Reaper_revives_stale_running_rows()
    {
        await using var host = await InMemoryKoanHost.Start();
        var registry = host.Services.GetRequiredService<JobTypeRegistry>();
        var queue = host.Services.GetRequiredService<IJobQueue>();
        var ct = CancellationToken.None;

        var now = DateTimeOffset.UtcNow;
        var stale = new StubJob
        {
            Id = $"stub-stale-{Guid.NewGuid():N}",
            Status = JobStatus.Running,
            LeasedUntil = now - TimeSpan.FromMinutes(1),
            Attempt = 1,
            StartedAt = now - TimeSpan.FromMinutes(5),
            Progress = 0.5,
            CurrentStep = 3,
            ProgressMessage = "halfway"
        };
        await stale.SaveSelf(ct);

        var recovered = await registry.ReapOrphansAll(queue, now, NullLogger.Instance, ct);

        recovered.Should().Be(1);

        var reloaded = await StubJob.Get(stale.Id, ct);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(JobStatus.Queued);
        reloaded.Attempt.Should().Be(2);
        reloaded.LeasedUntil.Should().BeNull();
        reloaded.StartedAt.Should().BeNull();
        reloaded.Progress.Should().Be(0d);
        reloaded.CurrentStep.Should().BeNull();
        reloaded.ProgressMessage.Should().Be("Recovered after lease lapsed.");
    }

    [Fact(DisplayName = "Reaper: leaves Running rows whose lease is still fresh alone")]
    public async Task Reaper_leaves_fresh_running_rows_alone()
    {
        await using var host = await InMemoryKoanHost.Start();
        var registry = host.Services.GetRequiredService<JobTypeRegistry>();
        var queue = host.Services.GetRequiredService<IJobQueue>();
        var ct = CancellationToken.None;

        var now = DateTimeOffset.UtcNow;
        var future = now + TimeSpan.FromMinutes(1);
        var fresh = new StubJob
        {
            Id = $"stub-fresh-{Guid.NewGuid():N}",
            Status = JobStatus.Running,
            LeasedUntil = future,
            Attempt = 1,
            Progress = 0.25,
            CurrentStep = 1,
            ProgressMessage = "in-flight"
        };
        await fresh.SaveSelf(ct);

        var recovered = await registry.ReapOrphansAll(queue, now, NullLogger.Instance, ct);

        recovered.Should().Be(0);

        var reloaded = await StubJob.Get(fresh.Id, ct);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(JobStatus.Running);
        reloaded.Attempt.Should().Be(1);
        reloaded.LeasedUntil.Should().Be(future);
        reloaded.Progress.Should().Be(0.25);
        reloaded.CurrentStep.Should().Be(1);
        reloaded.ProgressMessage.Should().Be("in-flight");
    }

    [Fact(DisplayName = "Reaper: treats Running rows with null LeasedUntil as stale (pre-migration safety)")]
    public async Task Reaper_treats_running_with_null_lease_as_stale()
    {
        // Rows persisted before the lease field existed (or by older runtime paths) carry a null
        // lease. The reaper has to treat those as orphans — otherwise a row that crashed out of
        // a pre-lease build would be wedged at Running forever.
        await using var host = await InMemoryKoanHost.Start();
        var registry = host.Services.GetRequiredService<JobTypeRegistry>();
        var queue = host.Services.GetRequiredService<IJobQueue>();
        var ct = CancellationToken.None;

        var now = DateTimeOffset.UtcNow;
        var preMigration = new StubJob
        {
            Id = $"stub-null-lease-{Guid.NewGuid():N}",
            Status = JobStatus.Running,
            LeasedUntil = null,
            Attempt = 1
        };
        await preMigration.SaveSelf(ct);

        var recovered = await registry.ReapOrphansAll(queue, now, NullLogger.Instance, ct);

        recovered.Should().Be(1);

        var reloaded = await StubJob.Get(preMigration.Id, ct);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(JobStatus.Queued);
        reloaded.Attempt.Should().Be(2);
        reloaded.LeasedUntil.Should().BeNull();
    }
}
