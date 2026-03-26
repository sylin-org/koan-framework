using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Core.Tests.Support;

/// <summary>
/// Minimal concrete Job subclass for unit-testing mappers, recipes, etc.
/// Never intended to be executed via the pipeline.
/// </summary>
public sealed class StubJob : Job<StubJob, string, string>
{
    protected override Task<string> Execute(string context, IJobProgress progress, CancellationToken cancellationToken)
        => Task.FromResult("done");
}
