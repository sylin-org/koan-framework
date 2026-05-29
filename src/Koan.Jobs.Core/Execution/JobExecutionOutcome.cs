using System;
using Koan.Jobs.Model;

namespace Koan.Jobs.Execution;

/// <summary>Outcome of one job-body run. The typed job writes its own <c>Result</c> field; this
/// only carries the terminal status and any error.</summary>
internal sealed record JobExecutionOutcome(JobExecutionStatus Status, Exception? Error);
