namespace Koan.Media.Core.Tests.Specs.Planner;

/// <summary>
/// Planner property tests covering the seven test areas the MEDIA-0005
/// §Test coverage requirements call out: kind threading, Sample
/// collapse, Sample no-op on Raster, encoder gate refusal, encoder
/// Accepts matrix, Vector forward-derive (and its sizing-free failure
/// mode), the ExtractFrame source-compat alias, FlattenTo expansion,
/// the KindMismatch payload shape, and the planner's never-reorder
/// invariant.
/// </summary>
public sealed class MediaPipelinePlannerSpec
{
    // ---- Fixtures -----------------------------------------------------

    /// <summary>Synthetic probe matching a static raster source.</summary>
    private static MediaInfo RasterProbe(int width = 800, int height = 600) =>
        new(
            Format: "png",
            Width: width,
            Height: height,
            FrameCount: 1,
            HasAlpha: false,
            ColorDepth: 8,
            ExifOrientation: null,
            HasIccProfile: false);

    /// <summary>Synthetic probe matching an animated raster source.</summary>
    private static MediaInfo AnimatedRasterProbe(int width = 800, int height = 600, int frames = 10) =>
        new(
            Format: "gif",
            Width: width,
            Height: height,
            FrameCount: frames,
            HasAlpha: true,
            ColorDepth: 8,
            ExifOrientation: null,
            HasIccProfile: false);

    /// <summary>
    /// The planner derives initial kind from probe.IsAnimated only —
    /// Raster vs AnimatedRaster. Vector and Timeline have no decoder
    /// yet (MEDIA-0006), so we exercise the Vector branch by handing
    /// the planner a pre-collapsed step list whose acceptance is
    /// kept Vector-compatible (the Sample step on Vector defers per
    /// MEDIA-0005 §4). For the dedicated Vector tests we still need a
    /// MediaInfo and a way to set the starting kind. The planner's
    /// only kind-sensitive read on probe is IsAnimated, so to force a
    /// Vector starting kind we synthesise the steps with a leading
    /// no-op and use the static raster probe — the dedicated Vector
    /// helper below threads the kind in through the planner's
    /// non-probe surface area, which is where Vector arrives anyway.
    /// </summary>
    private static MediaInfo RasterProbeForVectorScenarios() => RasterProbe();

    // The planner reads the source kind from probe.IsAnimated only.
    // Vector/Timeline arrive in MEDIA-0006 via a decoder that produces
    // those kinds directly. To exercise the planner's Vector branch we
    // call Plan with a probe and a step list whose first step bridges
    // into Vector — but no such step exists in the public surface, and
    // a custom step would be testing the planner's response to an
    // arbitrary MediaStep rather than the documented surface.
    //
    // The pragmatic answer: the planner's Vector handling is reachable
    // by constructing a one-off MediaStep that AcceptsFrom = All and
    // ProducesTo = Vector. We define VectorBridgeStep below for this
    // purpose; it represents the future SVG decoder's product without
    // pulling in MEDIA-0006.

    /// <summary>
    /// Test-only step that materialises a Vector source mid-pipeline.
    /// Stands in for the MEDIA-0006 SVG decoder so the planner's
    /// Vector branch can be exercised today. Accepts any input,
    /// produces Vector.
    /// </summary>
    private sealed record VectorBridgeStep()
        : MediaStep(PipelineStage.Decode, null, false)
    {
        public override void WriteFingerprint(System.Text.StringBuilder sb) =>
            sb.Append("vector-bridge()");
        public override KindSet AcceptsFrom => KindSet.All;
        public override MediaKind? ProducesTo => MediaKind.Vector;
    }

    // ---- 1. Kind threading -------------------------------------------

    [Fact]
    public void Plan_threads_Raster_kind_through_kindpreserving_steps()
    {
        var steps = new MediaStep[]
        {
            new ResizeStep(800, 600),
            new EncodeStep("webp", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            RasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("webp"));

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
        result.FinalKind.Should().Be(MediaKind.Raster);
        result.KindTrace.Should().Equal(MediaKind.Raster, MediaKind.Raster, MediaKind.Raster);
    }

    [Fact]
    public void Plan_threads_AnimatedRaster_kind_through_kindpreserving_steps()
    {
        var steps = new MediaStep[]
        {
            new ResizeStep(800, 600),
            new EncodeStep("webp", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            AnimatedRasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("webp"));

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
        result.FinalKind.Should().Be(MediaKind.AnimatedRaster);
        result.KindTrace.Should().Equal(MediaKind.AnimatedRaster, MediaKind.AnimatedRaster, MediaKind.AnimatedRaster);
    }

    // ---- 2. Sample collapses kind ------------------------------------

    [Fact]
    public void Plan_Sample_collapses_AnimatedRaster_to_Raster()
    {
        var steps = new MediaStep[]
        {
            new SampleStep(new FrameSelector.Index(0)),
            new ResizeStep(800, 600),
            new EncodeStep("jpeg", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            AnimatedRasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("jpeg"));

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
        result.FinalKind.Should().Be(MediaKind.Raster);
        // Trace records initial kind plus the kind out of each step.
        result.KindTrace.Should().Equal(
            MediaKind.AnimatedRaster, // initial
            MediaKind.Raster,         // after Sample
            MediaKind.Raster,         // after Resize (kind-preserving)
            MediaKind.Raster);        // after Encode (kind-preserving)
    }

    // ---- 3. Sample is no-op on Raster --------------------------------

    [Fact]
    public void Plan_Sample_is_noop_on_Raster()
    {
        var steps = new MediaStep[]
        {
            new SampleStep(new FrameSelector.Index(0)),
            new ResizeStep(800, 600),
            new EncodeStep("jpeg", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            RasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("jpeg"));

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
        result.FinalKind.Should().Be(MediaKind.Raster);
        result.KindTrace.Should().Equal(MediaKind.Raster, MediaKind.Raster, MediaKind.Raster, MediaKind.Raster);
    }

    // ---- 4. Encoder gate rejects -------------------------------------

    [Fact]
    public void Plan_encoder_gate_rejects_AnimatedRaster_into_jpeg()
    {
        var steps = new MediaStep[]
        {
            new ResizeStep(800, 600),
            new EncodeStep("jpeg", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            AnimatedRasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("jpeg"));

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNull();
        // Terminal-encoder gate failure indexes at steps.Count (post-last).
        result.Error!.StepIndex.Should().Be(steps.Length);
        result.Error.GotKind.Should().Be(MediaKind.AnimatedRaster);
        result.Error.ExpectedKinds.Should().Be(KindSet.Of(MediaKind.Raster));
        result.Error.Suggestion.Should().Contain("Sample");
        result.Error.Suggestion.Should().Contain("encode");
    }

    // ---- 5. WebP accepts animation -----------------------------------

    [Fact]
    public void Plan_webp_accepts_AnimatedRaster_through_terminal_gate()
    {
        var steps = new MediaStep[]
        {
            new ResizeStep(800, 600),
            new EncodeStep("webp", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            AnimatedRasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("webp"));

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
        result.FinalKind.Should().Be(MediaKind.AnimatedRaster);
    }

    // ---- 6. Vector forward-derive ------------------------------------

    [Fact]
    public void Plan_Vector_forwardderives_Rasterize_target_from_subsequent_Resize()
    {
        // Bridge into Vector, then Sample (deferred), Resize(640), Encode.
        // The planner should keep currentKind = Vector through Sample
        // (deferred Rasterize) and Resize (kind-preserving), then at the
        // terminal gate insert an implicit Rasterize PlannedStep carrying
        // the forward-derived target dimensions (640 from the Resize).
        var resize = new ResizeStep(640, 480);
        var steps = new MediaStep[]
        {
            new VectorBridgeStep(),
            new SampleStep(new FrameSelector.Index(0)),
            resize,
            new EncodeStep("webp", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            RasterProbe(), // initial probe.IsAnimated = false; the bridge re-kinds to Vector
            steps,
            EncoderAccepts.AcceptsFor("webp"));

        result.Ok.Should().BeTrue("planner should forward-derive Rasterize at the encoder boundary");
        result.Error.Should().BeNull();
        result.FinalKind.Should().Be(MediaKind.Raster);

        // The implicit Rasterize PlannedStep is appended after the
        // author's last step. Author intent (the four steps in order)
        // is preserved, and one implicit step trails it.
        result.Steps.Should().HaveCount(steps.Length + 1);
        result.Steps.Take(steps.Length).Select(p => p.Step).Should().Equal(steps);

        var implicitStep = result.Steps.Last();
        implicitStep.Implicit.Should().BeTrue();
        implicitStep.InputKind.Should().Be(MediaKind.Vector);
        implicitStep.OutputKind.Should().Be(MediaKind.Raster);
        implicitStep.ResolvedParams.Should().NotBeNull();
        implicitStep.ResolvedParams!["targetWidth"].Should().Be(640);
        implicitStep.ResolvedParams!["targetHeight"].Should().Be(480);
        implicitStep.Reason.Should().Contain("Rasterize");
    }

    // ---- 7. RasterizeRequiredButNoSizing -----------------------------

    [Fact]
    public void Plan_Vector_without_sizing_step_returns_RasterizeRequiredButNoSizing()
    {
        // Vector source reaches encoder boundary with no upstream
        // sizing step. The planner has no plausible default extent and
        // must reject the recipe at plan time.
        var steps = new MediaStep[]
        {
            new VectorBridgeStep(),
            new EncodeStep("webp", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            RasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("webp"));

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.StepIndex.Should().Be(steps.Length);
        result.Error.GotKind.Should().Be(MediaKind.Vector);
        result.Error.Suggestion.Should().Contain("sizing");
    }

    // ---- 8. ExtractFrame source-compat alias -------------------------

#pragma warning disable CS0618 // Type or member is obsolete
    [Fact]
    public void ExtractFrame_obsolete_builder_alias_produces_canonical_SampleStep()
    {
        // The [Obsolete] ExtractFrame(n) builder verb should emit the
        // canonical SampleStep(FrameSelector.Index(n)) so cache keys
        // and the planner see one vocabulary.
        var recipe = MediaRecipe.New()
            .ExtractFrame(2)
            .EncodeAs("jpeg", 80)
            .Build();

        recipe.Steps.Should().Contain(s => s is SampleStep);
        var sample = recipe.Steps.OfType<SampleStep>().Single();
        sample.Selector.Should().BeOfType<FrameSelector.Index>();
        ((FrameSelector.Index)sample.Selector).Frame.Should().Be(2);
    }
#pragma warning restore CS0618

    // ---- 9. FlattenTo planner expansion ------------------------------

    [Fact]
    public void Plan_FlattenTo_collapses_AnimatedRaster_to_Raster_through_gate()
    {
        // FlattenTo's ProducesTo is Raster — it is the explicit
        // destructive collapse. The planner should accept it through
        // the encoder gate without inserting an extra Sample step;
        // FlattenTo's per-step semantics ARE the collapse.
        var steps = new MediaStep[]
        {
            new FlattenToStep("jpeg", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            AnimatedRasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("jpeg"));

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
        result.FinalKind.Should().Be(MediaKind.Raster);
        // No implicit step inserted; the FlattenTo IS the collapse.
        result.Steps.Should().HaveCount(1);
        result.Steps[0].Step.Should().Be(steps[0]);
        result.Steps[0].Implicit.Should().BeFalse();
        result.Steps[0].InputKind.Should().Be(MediaKind.AnimatedRaster);
        result.Steps[0].OutputKind.Should().Be(MediaKind.Raster);
    }

    // ---- 10. KindMismatch payload shape ------------------------------

    [Fact]
    public void PlanError_payload_carries_StepIndex_ExpectedKinds_GotKind_and_Sample_suggestion()
    {
        // Trigger a terminal-encoder mismatch and assert all four
        // payload fields are populated with their stable values.
        var steps = new MediaStep[]
        {
            new ResizeStep(800, 600),
            new EncodeStep("jpeg", 80),
        };

        var result = MediaPipelinePlanner.Plan(
            AnimatedRasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("jpeg"));

        result.Ok.Should().BeFalse();
        var error = result.Error!;
        error.StepIndex.Should().Be(steps.Length); // zero-indexed terminal position
        error.ExpectedKinds.Should().Be(KindSet.Of(MediaKind.Raster));
        error.GotKind.Should().Be(MediaKind.AnimatedRaster);
        error.Suggestion.Should().NotBeNullOrWhiteSpace();
        error.Suggestion.Should().Contain("Sample"); // literal Sample.First insertion hint
    }

    [Fact]
    public void PlanError_for_perstep_admission_indexes_failing_step()
    {
        // AutoOrient rejects Vector — drive a per-step admission failure
        // and assert StepIndex points at the failing step's position.
        var steps = new MediaStep[]
        {
            new VectorBridgeStep(), // currentKind -> Vector
            new AutoOrientStep(),   // AcceptsFrom = {Raster, AnimatedRaster} -> mismatch at index 1
        };

        var result = MediaPipelinePlanner.Plan(
            RasterProbe(),
            steps,
            finalEncoderAccepts: null);

        result.Ok.Should().BeFalse();
        var error = result.Error!;
        error.StepIndex.Should().Be(1);
        error.GotKind.Should().Be(MediaKind.Vector);
        error.ExpectedKinds.Should().Be(KindSet.Of(MediaKind.Raster, MediaKind.AnimatedRaster));
        error.Suggestion.Should().Contain("Sample");
    }

    // ---- 11. Encoder Accepts matrix ----------------------------------

    [Theory]
    [InlineData("jpeg", new[] { MediaKind.Raster })]
    [InlineData("png", new[] { MediaKind.Raster })]
    [InlineData("bmp", new[] { MediaKind.Raster })]
    [InlineData("tiff", new[] { MediaKind.Raster })]
    [InlineData("webp", new[] { MediaKind.Raster, MediaKind.AnimatedRaster })]
    [InlineData("gif", new[] { MediaKind.Raster, MediaKind.AnimatedRaster })]
    public void EncoderAccepts_matrix_matches_documented_kinds(string slug, MediaKind[] expected)
    {
        EncoderAccepts.AcceptsFor(slug).Should().Be(KindSet.Of(expected));
    }

    [Fact]
    public void EncoderAccepts_unknown_slug_returns_empty_set()
    {
        EncoderAccepts.AcceptsFor("wat").Should().Be(KindSet.None);
        EncoderAccepts.AcceptsFor("").Should().Be(KindSet.None);
    }

    // ---- 12. Planner never reorders ----------------------------------

    [Fact]
    public void Plan_never_reorders_author_steps_even_when_kindequivalent()
    {
        // Resize(800) → Sample → Resize(400) → EncodeAs(jpeg).
        // In raster space Sample-then-Resize and Resize-then-Sample are
        // commute-equivalent — but the planner respects author order.
        var s1 = new ResizeStep(800, 600);
        var s2 = new SampleStep(new FrameSelector.Index(0));
        var s3 = new ResizeStep(400, 300);
        var s4 = new EncodeStep("jpeg", 80);
        var steps = new MediaStep[] { s1, s2, s3, s4 };

        var result = MediaPipelinePlanner.Plan(
            AnimatedRasterProbe(),
            steps,
            EncoderAccepts.AcceptsFor("jpeg"));

        result.Ok.Should().BeTrue();
        result.Steps.Should().HaveCount(4);
        result.Steps.Select(p => p.Step).Should().Equal(s1, s2, s3, s4);
        // And the trace records the AnimatedRaster -> Raster collapse
        // happening between index 1 (after Resize(800)) and index 2
        // (after Sample), not anywhere else.
        result.KindTrace.Should().Equal(
            MediaKind.AnimatedRaster, // initial
            MediaKind.AnimatedRaster, // after Resize(800) — kind-preserving
            MediaKind.Raster,         // after Sample — collapse
            MediaKind.Raster,         // after Resize(400) — kind-preserving
            MediaKind.Raster);        // after Encode — kind-preserving
    }
}
