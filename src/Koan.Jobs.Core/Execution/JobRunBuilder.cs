using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;

namespace Koan.Jobs.Execution;

public sealed class JobRunBuilder<TJob, TContext, TResult>
    where TJob : Job<TJob, TContext, TResult>, new()
{
    private readonly IServiceProvider _services;
    private readonly Type _jobType;
    private readonly TContext _context;
    private readonly string? _correlationId;
    private readonly CancellationToken _builderToken;
    private readonly List<Action<TJob>> _mutators = new();

    private JobStorageMode _storageMode = JobStorageMode.InMemory;
    private bool? _audit;
    private string? _source;
    private string? _partition;

    internal JobRunBuilder(
        IServiceProvider services,
        Type jobType,
        TContext context,
        string? correlationId,
        CancellationToken builderToken)
    {
        _services = services;
        _jobType = jobType;
        _context = context;
        _correlationId = correlationId;
        _builderToken = builderToken;
    }

    public JobRunBuilder<TJob, TContext, TResult> Persist(string? source = null, string? partition = null)
    {
        _storageMode = JobStorageMode.Entity;
        _source = source;
        _partition = partition;
        return this;
    }

    public JobRunBuilder<TJob, TContext, TResult> Audit(bool enabled = true)
    {
        _audit = enabled;
        return this;
    }

    public JobRunBuilder<TJob, TContext, TResult> With(Action<TJob> configure)
    {
        if (configure != null)
            _mutators.Add(configure);
        return this;
    }

    public Task<TJob> Run(CancellationToken cancellationToken = default)
    {
        var descriptor = BuildDescriptor(cancellationToken);
        return JobRunDispatcher.Run(descriptor);
    }

    internal JobRunRequest<TJob, TContext, TResult> BuildDescriptor(CancellationToken runToken)
    {
        var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_builderToken, runToken).Token;
        return new JobRunRequest<TJob, TContext, TResult>(
            _jobType,
            _context,
            _correlationId,
            _storageMode,
            _audit,
            _source,
            _partition,
            _mutators.AsReadOnly(),
            _services,
            linkedToken);
    }
}
