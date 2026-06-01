using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// Per MEDIA-0004 §11: one decode, N encodes. Each branch's transforms
/// must apply only to its own output (no bleed across branches).
/// </summary>
public sealed class MaterializeBundleSpec
{
    [Fact]
    public async Task Single_branch_bundle_produces_one_variant()
    {
        await using var src = Fixtures.WideJpeg(width: 800, height: 600);
        var bundle = await src.AsMedia().MaterializeAsync(b => b
            .Add("thumb", v => v.ResizeFit(200, 150).EncodeAs("webp", 80)));

        bundle.Variants.Should().HaveCount(1);
        bundle.Variants["thumb"].Format.Should().Be("webp");
    }

    [Fact]
    public async Task Multi_branch_bundle_produces_all_variants()
    {
        await using var src = Fixtures.WideJpeg(width: 1200, height: 800);
        var bundle = await src.AsMedia().MaterializeAsync(b => b
            .Add("display", v => v)
            .Add("thumb-400", v => v.ResizeFit(400, 400).EncodeAs("webp", 70))
            .Add("poster", v => v.Sample(new FrameSelector.Index(0)).EncodeAs("png", Quality.Lossless)));

        bundle.Variants.Should().ContainKey("display");
        bundle.Variants.Should().ContainKey("thumb-400");
        bundle.Variants.Should().ContainKey("poster");

        bundle.Variants["display"].Width.Should().Be(1200);
        bundle.Variants["display"].Height.Should().Be(800);
        bundle.Variants["thumb-400"].Width.Should().BeLessOrEqualTo(400);
        bundle.Variants["thumb-400"].Format.Should().Be("webp");
        bundle.Variants["poster"].Format.Should().Be("png");
    }

    [Fact]
    public async Task Branches_do_not_bleed_into_each_other()
    {
        await using var src = Fixtures.WideJpeg(width: 800, height: 600);
        var bundle = await src.AsMedia().MaterializeAsync(b => b
            .Add("small", v => v.ResizeFit(100, 100))
            .Add("large", v => v.ResizeFit(400, 400))
            .Add("original", v => v));

        // 'original' branch should retain source dimensions despite siblings resizing
        bundle.Variants["original"].Width.Should().Be(800);
        bundle.Variants["original"].Height.Should().Be(600);
        // 'small' bounded ≤100 on the longer side
        bundle.Variants["small"].Width.Should().BeLessOrEqualTo(100);
        // 'large' bounded ≤400 on the longer side
        bundle.Variants["large"].Width.Should().BeLessOrEqualTo(400);
    }

    [Fact]
    public async Task Animated_source_preserves_animation_per_branch()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3, width: 200, height: 200);
        var bundle = await src.AsMedia().MaterializeAsync(b => b
            .Add("display", v => v.ResizeFit(100, 100))
            .Add("poster", v => v.Sample(new FrameSelector.Index(0)).EncodeAs("png")));

        bundle.Variants["display"].FrameCount.Should().Be(3, "display branch keeps animation");
        bundle.Variants["poster"].FrameCount.Should().Be(1, "poster branch collapsed via ExtractFrame");
    }

    [Fact]
    public async Task Empty_bundle_throws()
    {
        await using var src = Fixtures.WideJpeg();
        var act = async () => await src.AsMedia().MaterializeAsync(_ => { });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Duplicate_variant_name_throws()
    {
        await using var src = Fixtures.WideJpeg();
        var act = async () => await src.AsMedia().MaterializeAsync(b => b
            .Add("same", v => v)
            .Add("same", v => v));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Pipeline_consumed_after_materialize()
    {
        await using var src = Fixtures.WideJpeg();
        var pipeline = src.AsMedia();
        await pipeline.MaterializeAsync(b => b.Add("a", v => v));
        var act = async () => await pipeline.MaterializeAsync(b => b.Add("b", v => v));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
