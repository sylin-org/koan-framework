namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Observes canon pipeline execution for diagnostics and analytics.
/// </summary>
public interface ICanonPipelineObserver
{
    /// <summary>
    /// Invoked immediately before a pipeline phase executes.
    /// </summary>
    ValueTask BeforePhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invoked after a pipeline phase completes successfully.
    /// </summary>
    ValueTask AfterPhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CanonizationEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invoked when a pipeline phase raises an exception.
    /// </summary>
    ValueTask OnErrorAsync(CanonPipelinePhase phase, ICanonPipelineContext context, Exception exception, CancellationToken cancellationToken = default);
}
