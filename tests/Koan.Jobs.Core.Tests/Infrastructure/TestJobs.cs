using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Execution;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Core.Tests.Infrastructure;

public sealed record SampleJobContext(string Payload);

public sealed class SampleJob : Job<SampleJob, SampleJobContext, string>
{
    protected override async Task<string> Execute(SampleJobContext context, IJobProgress progress, CancellationToken cancellationToken)
    {
        progress.Report(0.25, "Started");
        await Task.Delay(10, cancellationToken);
        progress.Report(0.75, "Processing");
        await Task.Delay(10, cancellationToken);
        progress.Report(1.0, "Completed");
        return context.Payload.ToUpperInvariant();
    }
}

[RetryPolicy(MaxAttempts = 3, Strategy = RetryStrategy.Fixed, InitialDelaySeconds = 0)]
public sealed class FlakyJob : Job<FlakyJob, SampleJobContext, string>
{
    private static int _attempts;

    public static void Reset() => Interlocked.Exchange(ref _attempts, 0);

    protected override Task<string> Execute(SampleJobContext context, IJobProgress progress, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _attempts);
        if (attempt < 2)
            throw new InvalidOperationException("Boom");
        return Task.FromResult(context.Payload);
    }
}
