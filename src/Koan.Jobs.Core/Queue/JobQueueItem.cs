using System;

namespace Koan.Jobs.Queue;

/// <summary>
/// A unit of dispatch (JOBS-0003): the job's id, its concrete type (used to resolve the typed
/// dispatcher and the <see cref="Koan.Data.Core.Model.Entity{T}"/> set), and the resolved lane.
/// The job's data lives in its own set; the queue only carries what dispatch needs.
/// </summary>
internal readonly record struct JobQueueItem(string JobId, Type JobType, string? Lane);
