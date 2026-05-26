using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// Per MEDIA-0004 §1: stage execution order is structural, not URL/builder
/// invocation order. <c>?w=600&amp;format=png</c> and <c>?format=png&amp;w=600</c>
/// must produce identical bytes.
/// </summary>
public sealed class PipelineStageOrderSpec
{
    [Fact]
    public async Task Same_steps_in_different_invocation_order_produce_identical_output()
    {
        // Resize then EncodeAs vs EncodeAs then Resize — both should
        // produce identical bytes because encode is always terminal.
        await using var a = Fixtures.WideJpeg(width: 800, height: 600);
        await using var b = Fixtures.WideJpeg(width: 800, height: 600);

        var first = await a.AsMedia().Resize(400, 300).EncodeAs("png").ToBytesAsync();
        var second = await b.AsMedia().EncodeAs("png").Resize(400, 300).ToBytesAsync();

        first.Bytes.Length.Should().Be(second.Bytes.Length);
        first.Bytes.Should().Equal(second.Bytes,
            "stage ordering is structural — invocation order must not affect output");
    }

    [Fact]
    public async Task Resize_runs_before_encode_regardless_of_invocation_order()
    {
        await using var src = Fixtures.WideJpeg(width: 1200, height: 800);

        // Build the pipeline in "wrong" order: encode declared first.
        var output = await src.AsMedia().EncodeAs("png").Resize(300, 200).ToBytesAsync();

        // The resize ran (proving stage ordering): output is 300x200, not 1200x800
        output.Width.Should().Be(300);
        output.Height.Should().Be(200);
        output.Format.Should().Be("png", "encode is terminal — runs after resize");
    }

    [Fact]
    public async Task Single_slot_stages_are_replaced_on_duplicate_calls()
    {
        await using var src = Fixtures.WideJpeg(width: 800, height: 600);

        var output = await src.AsMedia()
            .Resize(400, 300)
            .Resize(200, 150)   // single-slot replace
            .EncodeAs("png")
            .EncodeAs("webp")    // single-slot replace
            .ToBytesAsync();

        output.Width.Should().Be(200);
        output.Height.Should().Be(150);
        output.Format.Should().Be("webp");
    }

    [Fact]
    public async Task AutoOrient_runs_by_default_when_no_orient_step_declared()
    {
        // Pure no-op pipeline still implicitly auto-orients.
        await using var src = Fixtures.WideJpeg(width: 200, height: 100);
        var output = await src.AsMedia().ToBytesAsync();
        output.Width.Should().Be(200);
        output.Height.Should().Be(100);
    }
}
