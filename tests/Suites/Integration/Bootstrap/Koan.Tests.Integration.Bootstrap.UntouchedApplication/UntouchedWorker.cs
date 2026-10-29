using Koan.Core.BackgroundServices;

namespace Koan.Tests.Integration.Bootstrap.UntouchedApplication;

/// <summary>
/// A background service of an application assembly that no code has touched before its host is composed. It reports
/// that the orchestrator started it, then runs until the host stops.
/// </summary>
[KoanBackgroundService(RunInTesting = true)]
public sealed class UntouchedWorker : IKoanBackgroundService
{
    private static readonly TaskCompletionSource StartedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a host's orchestrator has started this service.</summary>
    public static Task Started => StartedSource.Task;

    public string Name => nameof(UntouchedWorker);

    public async Task Execute(CancellationToken cancellationToken)
    {
        StartedSource.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
