using Koan.Media.Core.Fonts;
using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Fonts;

/// <summary>
/// Text overlays draw a person's words in any script. The repository bundles no font, so these run
/// against a system font that has Arabic and skip on a machine without one.
/// </summary>
public sealed class TextOverlaySpec
{
    private static readonly string[] Candidates =
    [
        @"C:\Windows\Fonts\arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
    ];

    private static KoanFontRegistry Fonts()
    {
        var path = Candidates.FirstOrDefault(File.Exists);
        if (path is null) Assert.Skip("No system font with Arabic glyphs on this machine.");
        return new KoanFontRegistry().Register("default", path!);
    }

    private static async Task<TestImage> Render(KoanFontRegistry fonts, string text, Position? position = null)
    {
        await using var host = Fixtures.SolidPng(600, 200, new Rgba(0, 0, 0));
        var output = await host.AsMedia(fonts: fonts)
            .OverlayText(text, fontSize: 48, position: position ?? Position.Center)
            .EncodeAs("png")
            .ToBytesAsync();
        return TestImage.Load(output.Bytes);
    }

    private static int Inked(TestImage img, int fromX = 0, int? toX = null)
    {
        var inked = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = fromX; x < (toX ?? img.Width); x++)
                if (img[x, y].R > 128) inked++;
        return inked;
    }

    [Fact]
    public async Task A_latin_title_draws()
    {
        using var img = await Render(Fonts(), "Mending with gold");
        Inked(img).Should().BeGreaterThan(1000);
    }

    [Fact]
    public async Task Arabic_is_shaped_rather_than_drawn_as_isolated_letters()
    {
        // Joined forms differ from the isolated letters a renderer without shaping would draw: the same
        // letters separated by a zero-width non-joiner stay isolated, so the two pictures differ.
        var fonts = Fonts();
        using var joined = await Render(fonts, "\u0628\u0628\u0628\u0628");
        using var isolated = await Render(fonts, "\u0628\u200C\u0628\u200C\u0628\u200C\u0628");

        Inked(joined).Should().BeGreaterThan(100);
        Inked(joined).Should().NotBe(Inked(isolated));
    }

    [Fact]
    public async Task Combining_marks_stay_on_their_letter()
    {
        // An e and a combining acute occupy one letter's advance, drawing more ink in the same place.
        var fonts = Fonts();
        using var marked = await Render(fonts, "e\u0301", Position.TopLeft);
        using var plain = await Render(fonts, "e", Position.TopLeft);

        Inked(marked).Should().BeGreaterThan(Inked(plain));
        Inked(marked, fromX: 60).Should().Be(0, "the mark adds no second letter beside the first");
    }

    [Fact]
    public async Task A_mixed_title_runs_right_to_left_with_latin_reading_forward()
    {
        // Paragraph is right-to-left: the Arabic word is on the right, the Latin word to its left,
        // and the Latin word is not mirrored, so it starts with the tall T at its own left.
        var fonts = Fonts();
        using var mixed = await Render(fonts, "\u0645\u0631\u062D\u0628\u0627 Tangent", Position.TopLeft);
        using var latin = await Render(fonts, "Tangent", Position.TopLeft);

        Inked(mixed).Should().BeGreaterThan(Inked(latin));
        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 60; y++)
            {
                (mixed[x, y].R > 128).Should().Be(latin[x, y].R > 128, $"the Latin word starts the line at ({x},{y})");
            }
        }
    }

    private static async Task<TestImage> OnHost(KoanFontRegistry fonts, string text, Position position)
    {
        await using var host = Fixtures.SolidPng(900, 300, new Rgba(0, 0, 0));
        var output = await host.AsMedia(fonts: fonts)
            .OverlayText(text, fontSize: 48, position: position)
            .EncodeAs("png")
            .ToBytesAsync();
        return TestImage.Load(output.Bytes);
    }

    private static (int Left, int Top, int Right, int Bottom) Ink(TestImage img)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < img.Height; y++)
        {
            for (var x = 0; x < img.Width; x++)
            {
                if (img[x, y].R <= 128) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        }
        return (left, top, right, bottom);
    }

    /// <summary>The columns where two renders of the same layout differ.</summary>
    private static (int Min, int Max) DifferingColumns(TestImage a, TestImage b)
    {
        int min = int.MaxValue, max = -1;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                if (a[x, y].R == b[x, y].R) continue;
                min = Math.Min(min, x);
                max = Math.Max(max, x);
            }
        }
        return (min, max);
    }

    [Fact]
    public async Task An_anchor_places_the_words_not_the_fonts_box()
    {
        var fonts = Fonts();

        using var topLeft = await OnHost(fonts, "Mending", Position.TopLeft);
        var ink = Ink(topLeft);
        ink.Left.Should().BeInRange(2, 9, "a small margin and the first letter's bearing");
        ink.Top.Should().BeInRange(2, 12, "a small margin and the room above a capital");

        using var bottomRight = await OnHost(fonts, "Mending", Position.BottomRight);
        ink = Ink(bottomRight);
        (899 - ink.Right).Should().BeLessThanOrEqualTo(1);
        (299 - ink.Bottom).Should().BeLessThanOrEqualTo(1);

        using var center = await OnHost(fonts, "Mending", Position.Center);
        ink = Ink(center);
        ((ink.Left + ink.Right) / 2.0).Should().BeApproximately(449.5, 4);
        ((ink.Top + ink.Bottom) / 2.0).Should().BeApproximately(149.5, 6);
    }

    [Fact]
    public async Task A_descender_is_not_clipped()
    {
        // The tail of a g at the bottom-right anchor stays on the canvas, below where an o ends.
        var fonts = Fonts();
        using var g = await OnHost(fonts, "g", Position.TopLeft);
        using var o = await OnHost(fonts, "o", Position.TopLeft);
        (Ink(g).Bottom - Ink(o).Bottom).Should().BeGreaterThan(5);
    }

    [Fact]
    public async Task A_mark_at_the_end_of_an_arabic_word_stays_on_its_letter()
    {
        // abc [ktab] def: the word runs right to left, so its last letter and that letter's damma are at its left end.
        var fonts = Fonts();
        using var without = await OnHost(fonts, "abc \u0643\u062A\u0627\u0628 def", Position.TopLeft);
        using var with = await OnHost(fonts, "abc \u0643\u062A\u0627\u0628\u064F def", Position.TopLeft);
        using var before = await OnHost(fonts, "abc", Position.TopLeft);
        using var upToWord = await OnHost(fonts, "abc \u0643\u062A\u0627\u0628", Position.TopLeft);

        var (wordLeft, wordRight) = (Ink(before).Right, Ink(upToWord).Right);
        var (min, max) = DifferingColumns(without, with);

        min.Should().BeGreaterThan(wordLeft, "the mark is inside the word");
        max.Should().BeLessThan((wordLeft + wordRight) / 2, "the mark is on the word's last letter, at its left end");
    }

    [Fact]
    public async Task A_mark_on_a_latin_word_in_a_right_to_left_line_stays_on_its_letter()
    {
        // [ktab] cafe\u0301 [ktab]: the Latin word reads forward in the middle, so the acute is on its right side.
        var fonts = Fonts();
        using var without = await OnHost(fonts, "\u0643\u062A\u0627\u0628 cafe \u0643\u062A\u0627\u0628", Position.TopLeft);
        using var with = await OnHost(fonts, "\u0643\u062A\u0627\u0628 cafe\u0301 \u0643\u062A\u0627\u0628", Position.TopLeft);

        var ink = Ink(without);
        var (min, max) = DifferingColumns(without, with);

        ((min + max) / 2.0).Should().BeGreaterThan((ink.Left + ink.Right) / 2.0, "the e ends the Latin word; a stray mark lands left of its c");
        (max - min).Should().BeLessThan(40, "one letter's mark, not a shifted word");
    }

    [Fact]
    public async Task A_line_feed_starts_a_new_line()
    {
        var fonts = Fonts();
        await using var one = Fixtures.SolidPng(600, 300, new Rgba(0, 0, 0));
        var output = await one.AsMedia(fonts: fonts)
            .OverlayText("Top\nBottom", fontSize: 48, position: Position.TopLeft)
            .EncodeAs("png")
            .ToBytesAsync();
        using var img = TestImage.Load(output.Bytes);

        var inkedRows = Enumerable.Range(0, img.Height).Where(y => Enumerable.Range(0, 300).Any(x => img[x, y].R > 128)).ToList();
        inkedRows.Max().Should().BeGreaterThan(70, "the second line sits below the first");
    }

    [Fact]
    public async Task Empty_text_leaves_the_host_as_it_was()
    {
        using var img = await Render(Fonts(), "");
        Inked(img).Should().Be(0);
    }

    [Fact]
    public async Task Text_overlay_naming_an_unregistered_font_says_which_are()
    {
        var fonts = Fonts();
        await using var host = Fixtures.SolidPng(100, 100, new Rgba(0, 0, 0));
        var act = async () => await host.AsMedia(fonts: fonts).OverlayText("Hi", font: "brand").EncodeAs("png").ToBytesAsync();
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("brand").And.Contain("default");
    }

    [Fact]
    public void Registering_a_file_that_is_not_a_font_is_refused()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a font");
            var act = () => new KoanFontRegistry().Register("default", path);
            act.Should().Throw<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
