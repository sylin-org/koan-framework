using Koan.Media.Core.Fonts;
using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Fonts;

/// <summary>
/// KoanFontRegistry contract + the error path that fires when a text
/// overlay is requested without a registered font. The "register from
/// a real TTF" happy path is in <see cref="TextOverlaySpec"/>; what we
/// cover here is the surface every consumer touches.
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
    public void An_unknown_name_is_not_contained()
    {
        var registry = new KoanFontRegistry();
        registry.Contains("missing").Should().BeFalse();
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
        await using var host = Fixtures.SolidPng(100, 100, new Rgba(0, 0, 0, 255));
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
        await using var host = Fixtures.SolidPng(100, 100, new Rgba(0, 0, 0, 255));
        var fonts = new KoanFontRegistry(); // declared but empty

        var act = async () => await host.AsMedia(fonts: fonts)
            .OverlayText("Hello")
            .EncodeAs("png")
            .ToBytesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

}
