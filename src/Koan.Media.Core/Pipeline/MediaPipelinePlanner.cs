using System.Collections.Immutable;
using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// One step in a validated execution plan. Carries the kind
/// transition (in / out) and any planner-resolved parameter overrides
/// (forward-derivation lands target dimensions on the upstream step
/// when a <see cref="SampleStep"/> on a Vector is deferred to
/// <c>Rasterize</c>). Per MEDIA-0005 §4.
/// </summary>
public sealed record PlannedStep(
    MediaStep Step,
    MediaKind InputKind,
    MediaKind OutputKind,
    bool Implicit = false,
    string? Reason = null,
    IReadOnlyDictionary<string, object>? ResolvedParams = null);

/// <summary>
/// Typed planner failure. Carries the failing step index, the kinds
/// the step (or encoder) accepts, the kind the planner had at that
/// position, and an actionable suggestion. Per MEDIA-0005 §5.
/// </summary>
public sealed record PlanError(
    int StepIndex,
    KindSet ExpectedKinds,
    MediaKind GotKind,
    string Suggestion);

/// <summary>
/// Result of <see cref="MediaPipelinePlanner.Plan"/>. On success
/// <see cref="Ok"/> is true and <see cref="Steps"/> holds the validated
/// plan; on failure <see cref="Error"/> is non-null.
/// </summary>
public sealed record PlanResult(
    bool Ok,
    IReadOnlyList<PlannedStep> Steps,
    MediaKind FinalKind,
    IReadOnlyList<MediaKind> KindTrace,
    PlanError? Error);

/// <summary>
/// Pure-function planner. Threads <c>currentKind</c> through the four-kind
/// taxonomy, never reorders author steps, validates step admission, and
/// (per MEDIA-0005 §4) forward-derives an implicit Rasterize insertion
/// when a Vector reaches a non-Vector encoder boundary.
/// </summary>
public static class MediaPipelinePlanner
{
    /// <summary>
    /// Plan the given step sequence against the source's probed kind
    /// and (optionally) the terminal encoder's <see cref="KindSet"/>.
    /// Pure function — does not touch I/O. Per MEDIA-0005 §4.
    /// </summary>
    /// <param name="probe">Probed source info (its <c>IsAnimated</c> drives initial kind selection).</param>
    /// <param name="steps">Author-ordered recipe steps. Not reordered.</param>
    /// <param name="finalEncoderAccepts">The terminal encoder's admission set, or null when ad-hoc / unspecified.</param>
    public static PlanResult Plan(
        MediaInfo probe,
        IReadOnlyList<MediaStep> steps,
        KindSet? finalEncoderAccepts)
    {
        if (steps is null) throw new ArgumentNullException(nameof(steps));

        // 1. Initial kind from probe. SVG (and any future Vector decoder)
        // declares Format = "svg" up front per MEDIA-0006; otherwise the
        // decoder-provided IsAnimated flag discriminates Raster vs
        // AnimatedRaster. Timeline arrives in a later ADR but the switch
        // is exhaustive at the kind-set level.
        var currentKind = ResolveInitialKind(probe);

        var planned = ImmutableArray.CreateBuilder<PlannedStep>(steps.Count);
        var trace = ImmutableArray.CreateBuilder<MediaKind>(steps.Count + 1);
        trace.Add(currentKind);

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            // 2a. Admission gate — strict.
            if (!step.AcceptsFrom.Contains(currentKind))
            {
                var error = new PlanError(
                    StepIndex: i,
                    ExpectedKinds: step.AcceptsFrom,
                    GotKind: currentKind,
                    Suggestion: $"Insert Sample.First before step {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
                return new PlanResult(
                    Ok: false,
                    Steps: planned.ToImmutable(),
                    FinalKind: currentKind,
                    KindTrace: trace.ToImmutable(),
                    Error: error);
            }

            // 2c. Sample on Vector — defer to Rasterize at encoder boundary.
            // currentKind stays Vector; the step is recorded but does not
            // change the running kind. The eventual Rasterize is inserted
            // by the encoder-boundary check below.
            MediaKind nextKind;
            string? reason = null;
            if (step is SampleStep && currentKind == MediaKind.Vector)
            {
                nextKind = MediaKind.Vector;
                reason = "Sample on Vector deferred to Rasterize at encoder boundary.";
            }
            else
            {
                nextKind = step.ProducesTo ?? currentKind;
            }

            planned.Add(new PlannedStep(
                Step: step,
                InputKind: currentKind,
                OutputKind: nextKind,
                Implicit: false,
                Reason: reason));
            trace.Add(nextKind);
            currentKind = nextKind;
        }

        // 4. Encoder admission gate.
        if (finalEncoderAccepts is { } accepts && !accepts.Contains(currentKind))
        {
            if (currentKind == MediaKind.Vector)
            {
                // 4. Forward-derive Rasterize target from the most recent sizing step.
                var sizing = FindLastSizing(steps);
                if (sizing is null)
                {
                    var rasterError = new PlanError(
                        StepIndex: steps.Count,
                        ExpectedKinds: accepts,
                        GotKind: currentKind,
                        Suggestion: "Vector source reached encoder boundary with no upstream sizing step. Add Resize(w, h) or Shape with explicit dimensions, or insert Sample.First before encode.");
                    return new PlanResult(
                        Ok: false,
                        Steps: planned.ToImmutable(),
                        FinalKind: currentKind,
                        KindTrace: trace.ToImmutable(),
                        Error: rasterError);
                }

                // Implicit Rasterize is recorded as a PlannedStep with no
                // backing author step; engine treats it as the Vector -> Raster
                // bridge using ResolvedParams for target extents. The step
                // payload is the discovered sizing step so executors that
                // recognize Rasterize can read its parameters from there.
                var resolved = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["targetWidth"] = sizing.Width ?? 0,
                    ["targetHeight"] = sizing.Height ?? 0,
                };
                planned.Add(new PlannedStep(
                    Step: sizing, // payload references the sizing step the planner forward-derived from
                    InputKind: MediaKind.Vector,
                    OutputKind: MediaKind.Raster,
                    Implicit: true,
                    Reason: $"Forward-derived Rasterize({sizing.Width}x{sizing.Height}) at encoder boundary.",
                    ResolvedParams: resolved));
                trace.Add(MediaKind.Raster);
                currentKind = MediaKind.Raster;
            }
            else
            {
                var error = new PlanError(
                    StepIndex: steps.Count,
                    ExpectedKinds: accepts,
                    GotKind: currentKind,
                    Suggestion: "Insert Sample.First before the terminal encode step.");
                return new PlanResult(
                    Ok: false,
                    Steps: planned.ToImmutable(),
                    FinalKind: currentKind,
                    KindTrace: trace.ToImmutable(),
                    Error: error);
            }
        }

        return new PlanResult(
            Ok: true,
            Steps: planned.ToImmutable(),
            FinalKind: currentKind,
            KindTrace: trace.ToImmutable(),
            Error: null);
    }

    /// <summary>
    /// Map the probed format to the initial running <see cref="MediaKind"/>.
    /// SVG (the first concrete Vector producer per MEDIA-0006) declares
    /// <c>Format = "svg"</c>; everything else falls back to the raster /
    /// animated-raster discrimination from the decoder's <c>IsAnimated</c>
    /// flag.
    /// </summary>
    private static MediaKind ResolveInitialKind(MediaInfo? probe)
    {
        if (probe is null) return MediaKind.Raster;
        if (string.Equals(probe.Format, "svg", StringComparison.OrdinalIgnoreCase))
        {
            return MediaKind.Vector;
        }
        return probe.IsAnimated ? MediaKind.AnimatedRaster : MediaKind.Raster;
    }

    /// <summary>
    /// Locate the most recent sizing step (<see cref="ResizeStep"/> or a
    /// <see cref="ShapeStep"/> with explicit pixel dimensions). Used to
    /// forward-derive a Rasterize target when a Vector reaches the
    /// encoder boundary.
    /// </summary>
    private static ResizeStep? FindLastSizing(IReadOnlyList<MediaStep> steps)
    {
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            if (steps[i] is ResizeStep rz && rz.Width.HasValue && rz.Height.HasValue)
            {
                return rz;
            }
            if (steps[i] is ShapeStep ss && ss.Crop is { } crop
                && (crop.Kind == CropSpecKind.Pixels || crop.Kind == CropSpecKind.PixelsWithOffset))
            {
                // Synthesize a ResizeStep from the shape's explicit pixel crop.
                return new ResizeStep(crop.Width, crop.Height);
            }
        }
        return null;
    }
}
