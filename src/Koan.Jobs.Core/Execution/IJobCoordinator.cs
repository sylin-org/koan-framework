using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;

namespace Koan.Jobs.Execution;

/// <summary>Submits and cancels jobs (JOBS-0003). Each job is its own <see cref="Koan.Data.Core.Model.Entity{T}"/>
/// set; submit persists it and enqueues it for the generic runtime.</summary>
internal interface IJobCoordinator
{
    Task<T> Submit<T>(T job, TimeSpan? delay, CancellationToken cancellationToken) where T : Job<T>, new();
    Task Cancel<T>(string jobId, CancellationToken cancellationToken) where T : Job<T>, new();
}
