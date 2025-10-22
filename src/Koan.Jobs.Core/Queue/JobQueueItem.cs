using System;
using Koan.Jobs.Model;

namespace Koan.Jobs.Queue;

internal readonly record struct JobQueueItem(
    string JobId,
    Type JobType,
    JobStorageMode StorageMode,
    string? Source,
    string? Partition,
    bool AuditExecutions,
    Type ContextType,
    Type ResultType);
