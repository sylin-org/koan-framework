using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Core.Tests.Support;

/// <summary>
/// Minimal concrete per-type job (JOBS-0003) for unit-testing the runtime plumbing. Carries a typed
/// payload and result as native fields; never intended to be executed via a real host here.
/// </summary>
public sealed class StubJob : Job<StubJob>
{
    public string Context { get; set; } = "";
    public string? Result { get; set; }

    protected override Task Do(IJobProgress progress, CancellationToken cancellationToken)
    {
        Result = "done";
        return Task.CompletedTask;
    }
}
