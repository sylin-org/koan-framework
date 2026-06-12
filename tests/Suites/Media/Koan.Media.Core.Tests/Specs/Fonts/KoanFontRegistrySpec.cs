using Koan.Media.Core.Fonts;
using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Koan.Media.Core.Tests.Specs.Fonts;

/// <summary>
/// KoanFontRegistry contract + the error path that fires when a text
/// overlay is requested without a registered font. The "register from
/// a real TTF" happy path requires a font file the test project doesn't
/// bundle, so it lives in integration territory; what we cover here is
/// the surface every consumer touches.
/// </summary>
public sealed class KoanFontRegistrySpec
{
    [Fact]
    public void Empty_registry_reports_no_fonts()
    {
        var registry = new KoanFontRegistry();
        registry.HasAny.Should().BeFalse();
        registry.Names.Should().BeEmpty();
    }

    [Fact]
    public void CreateFont_on_unknown_name_returns_null()
    {
        var registry = new KoanFontRegistry();
        registry.CreateFont("missing", 24f).Should().BeNull();
    }

    [Fact]
    public void Register_with_missing_path_throws_FileNotFoundException()
    {
        var registry = new KoanFontRegistry();
        var act = () => registry.Register("default", "/tmp/this-file-does-not-exist.ttf");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Register_with_empty_name_throws()
    {
        var registry = new KoanFontRegistry();
        var act = () => registry.Register("", "/tmp/whatever.ttf");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Text_overlay_without_registered_fonts_throws_with_clear_message()
    {
        // Engine-level: building a text overlay onto a host without any
        // font registry available must throw InvalidOperationException at
        // render time with a message that tells the operator what to do.
        await using var host = MakeSolidPng(100, 100, new Rgba32(0, 0, 0, 255));
        var act = async () => await host.AsMedia()
            .OverlayText("Hello")
            .EncodeAs("png")
            .ToBytesAsync();
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("no fonts");
        ex.Which.Message.Should().Contain("AddKoanFont");
    }

    [Fact]
    public async Task Text_overlay_with_empty_registry_throws()
    {
        await using var host = MakeSolidPng(100, 100, new Rgba32(0, 0, 0, 255));
        var fonts = new KoanFontRegistry(); // declared but empty

        var act = async () => await host.AsMedia(fonts: fonts)
            .OverlayText("Hello")
            .EncodeAs("png")
            .ToBytesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static MemoryStream MakeSolidPng(int width, int height, Rgba32 color)
    {
        using var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = color;
            }
        });
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }
}
