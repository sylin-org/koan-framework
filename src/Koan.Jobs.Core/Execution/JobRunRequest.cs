using System;
using System.Collections.Generic;
using System.Threading;
using Koan.Jobs.Model;

namespace Koan.Jobs.Execution;

internal sealed record JobRunRequest<TJob, TContext, TResult>(
    Type JobType,
    TContext Context,
    string? CorrelationId,
    JobStorageMode StorageMode,
    bool? Audit,
    string? Source,
    string? Partition,
    IReadOnlyList<Action<TJob>> Mutators,
    IServiceProvider Services,
    CancellationToken CancellationToken)
    where TJob : Job<TJob, TContext, TResult>, new();
