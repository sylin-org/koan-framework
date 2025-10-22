using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace S7.Meridian.Tests;

public sealed class JobCoordinatorTests
{
    [Fact]
    public async Task ScheduleAsync_ReusesPendingJobAndMergesDocuments()
    {
        await using var host = await UseInMemoryHostAsync();
        using var partition = EntityContext.Partition($"job-coordinator-{Guid.NewGuid():N}");
        var coordinator = new JobCoordinator(NullLogger<JobCoordinator>.Instance);
        var ct = CancellationToken.None;

        var pipeline = new DocumentPipeline { Name = "reuse-pipeline" };
        await pipeline.Save(ct);

        var initial = await coordinator.ScheduleAsync(pipeline.Id, new[] { "doc-1" }, ct);
        var reused = await coordinator.ScheduleAsync(pipeline.Id, new[] { "doc-1", "doc-2" }, ct);

        Assert.Equal(initial.Id, reused.Id);
        Assert.Equal(2, reused.DocumentIds.Count);
        Assert.Contains("doc-1", reused.DocumentIds);
        Assert.Contains("doc-2", reused.DocumentIds);

        var persisted = await ProcessingJob.Get(initial.Id, ct);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.Pending, persisted!.Status);
        Assert.Equal(2, persisted.DocumentIds.Count);
        Assert.Equal(2, persisted.TotalDocuments);
    }

    [Fact]
    public async Task TryCancelPendingAsync_CancelsPendingJob()
    {
        await using var host = await UseInMemoryHostAsync();
        using var partition = EntityContext.Partition($"job-cancel-{Guid.NewGuid():N}");
        var ct = CancellationToken.None;

        var pipeline = new DocumentPipeline { Name = "cancel-pipeline" };
        await pipeline.Save(ct);

        var job = new ProcessingJob
        {
            PipelineId = pipeline.Id,
            DocumentIds = new List<string> { "doc-1" },
            Status = JobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await job.Save(ct);

        var (updated, cancelled) = await ProcessingJob.TryCancelPendingAsync(job.Id, ct);

        Assert.True(cancelled);
        Assert.NotNull(updated);
        Assert.Equal(JobStatus.Cancelled, updated!.Status);

        var persisted = await ProcessingJob.Get(job.Id, ct);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.Cancelled, persisted!.Status);
    }

    [Fact]
    public async Task TryCancelPendingAsync_CancelsStaleProcessingJob()
    {
        await using var host = await UseInMemoryHostAsync();
        using var partition = EntityContext.Partition($"job-cancel-stale-{Guid.NewGuid():N}");
        var ct = CancellationToken.None;

        var pipeline = new DocumentPipeline { Name = "stale-pipeline" };
        await pipeline.Save(ct);

        var job = new ProcessingJob
        {
            PipelineId = pipeline.Id,
            DocumentIds = new List<string> { "doc-1" },
            Status = JobStatus.Processing,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15),
            ClaimedAt = DateTime.UtcNow.AddMinutes(-10),
            HeartbeatAt = DateTime.UtcNow.AddMinutes(-10),
            WorkerId = "worker-1"
        };

        await job.Save(ct);

        var (updated, cancelled) = await ProcessingJob.TryCancelPendingAsync(job.Id, ct);

        Assert.True(cancelled);
        Assert.NotNull(updated);
        Assert.Equal(JobStatus.Cancelled, updated!.Status);
        Assert.Null(updated.WorkerId);

        var persisted = await ProcessingJob.Get(job.Id, ct);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.Cancelled, persisted!.Status);
    }

    [Fact]
    public async Task TryCancelPendingAsync_DeniesActiveProcessingJob()
    {
        await using var host = await UseInMemoryHostAsync();
        using var partition = EntityContext.Partition($"job-cancel-active-{Guid.NewGuid():N}");
        var ct = CancellationToken.None;

        var pipeline = new DocumentPipeline { Name = "active-pipeline" };
        await pipeline.Save(ct);

        var job = new ProcessingJob
        {
            PipelineId = pipeline.Id,
            DocumentIds = new List<string> { "doc-1" },
            Status = JobStatus.Processing,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            ClaimedAt = DateTime.UtcNow.AddMinutes(-1),
            HeartbeatAt = DateTime.UtcNow,
            WorkerId = "worker-2"
        };

        await job.Save(ct);

        var (updated, cancelled) = await ProcessingJob.TryCancelPendingAsync(job.Id, ct);

        Assert.False(cancelled);
        Assert.NotNull(updated);
        Assert.Equal(JobStatus.Processing, updated!.Status);

        var persisted = await ProcessingJob.Get(job.Id, ct);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.Processing, persisted!.Status);
    }
}

internal static class JobCoordinatorTestHost
{
    public static async Task<IAsyncDisposable> UseInMemoryHostAsync()
    {
        var previousHost = AppHost.Current;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Provider"] = "Memory"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.AddKoan();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        var scopedProvider = scope.ServiceProvider;

        AppHost.Current = scopedProvider;
        KoanEnv.TryInitialize(scopedProvider);

        return new AsyncScope(previousHost, provider, scope);
    }

    private sealed class AsyncScope : IAsyncDisposable
    {
        private readonly IServiceProvider? _previous;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        public AsyncScope(IServiceProvider? previous, ServiceProvider provider, AsyncServiceScope scope)
        {
            _previous = previous;
            _provider = provider;
            _scope = scope;
        }

        public async ValueTask DisposeAsync()
        {
            if (ReferenceEquals(AppHost.Current, _scope.ServiceProvider))
            {
                AppHost.Current = _previous;
            }

            await _scope.DisposeAsync().ConfigureAwait(false);
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}
