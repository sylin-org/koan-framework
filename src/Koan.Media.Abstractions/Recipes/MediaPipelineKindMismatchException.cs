namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Thrown when the planner rejects a recipe at plan time because a step
/// (or the terminal encoder) does not accept the current media kind.
/// Per MEDIA-0005 §5: the only mode is strict — there is no lenient
/// "best-effort fallback" behavior.
/// </summary>
public sealed class MediaPipelineKindMismatchException : Exception
{
    /// <summary>Index of the step that failed admission (encoder is len(steps)).</summary>
    public int StepIndex { get; }

    /// <summary>The set the step (or encoder) accepts.</summary>
    public KindSet ExpectedKinds { get; }

    /// <summary>The current kind at the failing position.</summary>
    public MediaKind GotKind { get; }

    /// <summary>Human-readable, actionable suggestion (e.g. literal <c>Sample.First</c> insertion).</summary>
    public string Suggestion { get; }

    public MediaPipelineKindMismatchException(
        int stepIndex,
        KindSet expectedKinds,
        MediaKind gotKind,
        string suggestion)
        : base(BuildMessage(stepIndex, expectedKinds, gotKind, suggestion))
    {
        StepIndex = stepIndex;
        ExpectedKinds = expectedKinds;
        GotKind = gotKind;
        Suggestion = suggestion;
    }

    private static string BuildMessage(int stepIndex, KindSet expected, MediaKind got, string suggestion) =>
        $"Media pipeline kind mismatch at step {stepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}: "
        + $"got {got}, expected one of {expected}. {suggestion}";
}
