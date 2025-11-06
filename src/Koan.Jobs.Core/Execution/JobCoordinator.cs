using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Events;
using Koan.Jobs.Model;
using Koan.Jobs.Options;
using Koan.Jobs.Queue;
using Koan.Jobs.Store;
using Koan.Jobs.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Execution;

internal sealed class JobCoordinator : IJobCoordinator
{
    private readonly IJobStoreResolver _resolver;
    private readonly JobIndexCache _index;
    private readonly IJobQueue _queue;
    private readonly IJobEventPublisher _eventPublisher;
    private readonly JobsOptions _options;
    private readonly ILogger<JobCoordinator> _logger;

    public JobCoordinator(
        IJobStoreResolver resolver,
        JobIndexCache index,
        IJobQueue queue,
        IJobEventPublisher eventPublisher,
        IOptions<JobsOptions> options,
        ILogger<JobCoordinator> logger)
    {
        _resolver = resolver;
        _index = index;
        _queue = queue;
        _eventPublisher = eventPublisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TJob> Run<TJob, TContext, TResult>(JobRunRequest<TJob, TContext, TResult> request)
        where TJob : Job<TJob, TContext, TResult>, new()
    {
        var cancellationToken = request.CancellationToken;
        var storageMode = request.StorageMode switch
        {
            JobStorageMode.Entity => JobStorageMode.Entity,
            _ => JobStorageMode.InMemory
        };

        var source = request.Source ?? _options.DefaultSource;
        var partition = request.Partition ?? _options.DefaultPartition;
        var audit = request.Audit ?? _options.AuditByDefault;

        var metadata = new JobStoreMetadata(storageMode, source, partition, audit, _options.SerializerOptions);
        var store = _resolver.Resolve(storageMode);

        var job = new TJob
        {
            Status = JobStatus.Queued,
            QueuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            CorrelationId = request.CorrelationId,
            Progress = 0d
        };

        job.Context = request.Context;

        foreach (var mutator in request.Mutators)
        {
            mutator(job);
        }

        if (string.IsNullOrWhiteSpace(job.Name))
            job.Name = typeof(TJob).Name;

        _logger.LogDebug("Queueing job {JobId} ({JobType}) with storage {StorageMode}", job.Id, typeof(TJob).Name, storageMode);

        var saved = await store.CreateAsync(job, metadata, cancellationToken);
        if (saved is not TJob typed)
            throw new InvalidOperationException($"Store returned unexpected job type {saved.GetType().FullName}." );

        await _eventPublisher.PublishQueuedAsync(typed, cancellationToken);

        var baseType = typeof(TJob).BaseType;
        var genericArguments = baseType?.IsGenericType == true ? baseType.GetGenericArguments() : Array.Empty<Type>();
        var contextType = genericArguments.Length > 1 ? genericArguments[1] : typeof(object);
        var resultType = genericArguments.Length > 2 ? genericArguments[2] : typeof(object);

        var queueItem = new JobQueueItem(
            typed.Id,
            typeof(TJob),
            storageMode,
            source,
            partition,
            audit,
            contextType,
            resultType);

        await _queue.EnqueueAsync(queueItem, cancellationToken);
        return typed;
    }

    public async Task<TJob?> Refresh<TJob, TContext, TResult>(string jobId, CancellationToken cancellationToken)
        where TJob : Job<TJob, TContext, TResult>, new()
    {
        var (store, metadata, _) = ResolveStore(jobId, JobStorageMode.InMemory);
        var job = await store.GetAsync(jobId, metadata, cancellationToken);
        return job as TJob;
    }

    public async Task Cancel<TJob, TContext, TResult>(string jobId, CancellationToken cancellationToken)
        where TJob : Job<TJob, TContext, TResult>, new()
    {
        var (store, metadata, mode) = ResolveStore(jobId, _options.DefaultStore);
        var job = await store.GetAsync(jobId, metadata, cancellationToken);
        if (job == null)
            return;

        if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
            return;

        _logger.LogInformation("Cancelling job {JobId} (status: {Status})", job.Id, job.Status);

        if (!_index.TryGet(jobId, out var entry))
        {
            entry = new JobIndexEntry(jobId, mode, metadata.Source, metadata.Partition, metadata.Audit, job.GetType());
            _index.Set(entry);
        }
        entry.CancellationRequested = true;

        if (job.Status is JobStatus.Created or JobStatus.Queued)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Duration = job.CompletedAt - job.CreatedAt;
            await store.UpdateAsync(job, metadata, cancellationToken);
            await _eventPublisher.PublishCancelledAsync(job, cancellationToken);
        }
        else
        {
            await store.UpdateAsync(job, metadata, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<JobExecution>> GetExecutionsAsync(string jobId, CancellationToken cancellationToken)
    {
        var (store, metadata, _) = ResolveStore(jobId, _options.DefaultStore);
        return await store.ListExecutionsAsync(jobId, metadata, cancellationToken);
    }

    private (IJobStore Store, JobStoreMetadata Metadata, JobStorageMode Mode) ResolveStore(string jobId, JobStorageMode fallback)
    {
        if (_index.TryGet(jobId, out var entry))
        {
            var metadata = new JobStoreMetadata(entry.StorageMode, entry.Source, entry.Partition, entry.AuditEnabled, _options.SerializerOptions);
            return (_resolver.Resolve(entry.StorageMode), metadata, entry.StorageMode);
        }

        var metadataDefault = new JobStoreMetadata(fallback, _options.DefaultSource, _options.DefaultPartition, _options.AuditByDefault, _options.SerializerOptions);
        return (_resolver.Resolve(fallback), metadataDefault, fallback);
    }
}
