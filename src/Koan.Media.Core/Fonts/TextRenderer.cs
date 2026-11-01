using System.Text;
using HarfBuzzSharp;
using Koan.Media.Abstractions.Recipes;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Buffer = HarfBuzzSharp.Buffer;

namespace Koan.Media.Core.Fonts;

/// <summary>
/// Draws a person's words in any script: lines split on line feeds, each line split into runs of one script and
/// direction, each run shaped, and the runs put in visual order. Ligatures, contextual forms and combining marks
/// come from shaping; a title mixing Arabic and Latin reads correctly because of the run order.
/// </summary>
internal static class TextRenderer
{
    /// <summary>The largest bitmap a text layer takes on either edge; anything past it is clipped.</summary>
    private const int MaxEdge = 8192;

    private const int Margin = 2;

    public static SKBitmap Render(string text, SKTypeface typeface, float size, BackgroundColor color)
    {
        using var font = new SKFont(typeface, size)
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.None,
        };
        using var shaper = new SKShaper(typeface);

        var lines = text.ReplaceLineEndings("\n").Split('\n')
            .Select(line => ShapeLine(line, shaper, font))
            .ToList();
        var widest = lines.Max(l => l.Width);

        using var builder = new SKTextBlobBuilder();
        var baseline = 0f;
        foreach (var line in lines)
        {
            var x = line.RightToLeft ? widest - line.Width : 0f;
            foreach (var run in line.Runs)
            {
                var points = run.Points.Select(p => new SKPoint(x + p.X, baseline + p.Y)).ToArray();
                builder.AddPositionedRun(run.Glyphs, font, points);
                x += run.Width;
            }
            baseline += font.Spacing;
        }

        using var blob = builder.Build();
        var bounds = blob?.Bounds ?? SKRect.Empty;
        var width = Math.Min(MaxEdge, (int)Math.Ceiling(bounds.Width) + Margin * 2);
        var height = Math.Min(MaxEdge, (int)Math.Ceiling(bounds.Height) + Margin * 2);

        var bitmap = Pipeline.RasterImage.Allocate(width, height, colorSpace: null);
        if (blob is null) return bitmap;
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(color.R, color.G, color.B, color.A),
        };
        canvas.DrawText(blob, Margin - bounds.Left, Margin - bounds.Top, paint);
        return bitmap;
    }

    private static ShapedLine ShapeLine(string line, SKShaper shaper, SKFont font)
    {
        var (runs, rightToLeft) = TextRuns.Split(line);
        var shaped = new List<ShapedRun>(runs.Count);
        foreach (var run in runs)
        {
            using var buffer = new Buffer();
            buffer.AddUtf16(line, run.Start, run.Length);
            buffer.Direction = run.Level % 2 == 1 ? Direction.RightToLeft : Direction.LeftToRight;
            buffer.Script = run.Script;
            buffer.Language = Language.Default;
            var result = shaper.Shape(buffer, font);
            shaped.Add(new ShapedRun(result.Codepoints.Select(g => (ushort)g).ToArray(), result.Points, result.Width));
        }
        return new ShapedLine(shaped, shaped.Sum(r => r.Width), rightToLeft);
    }

    private sealed record ShapedRun(ushort[] Glyphs, SKPoint[] Points, float Width);

    private sealed record ShapedLine(IReadOnlyList<ShapedRun> Runs, float Width, bool RightToLeft);

    /// <summary>
    /// Direction resolution for one line, following the Unicode bidirectional algorithm where titles need it:
    /// the paragraph takes the direction of its first strong character, numbers keep left-to-right order inside
    /// right-to-left text, neutrals between two like directions join them and otherwise take the paragraph's,
    /// and runs reorder level by level. Embedding controls are not interpreted.
    /// </summary>
    private static class TextRuns
    {
        internal readonly record struct Run(int Start, int Length, int Level, Script Script);

        private enum Class { Left, Right, Number, Neutral }

        public static (List<Run> Runs, bool RightToLeft) Split(string line)
        {
            var functions = UnicodeFunctions.Default;
            var units = new List<(int Start, int Length, Class Class, Script Script)>();
            var position = 0;
            foreach (var rune in line.EnumerateRunes())
            {
                var script = functions.GetScript(rune.Value);
                units.Add((position, rune.Utf16SequenceLength, Classify(rune, script), script));
                position += rune.Utf16SequenceLength;
            }

            var rightToLeft = units.Select(u => u.Class).FirstOrDefault(c => c is Class.Left or Class.Right, Class.Left) == Class.Right;
            var levels = Resolve(units.Select(u => u.Class).ToArray(), rightToLeft);

            var runs = new List<Run>();
            var inherited = Script.Common;
            for (var i = 0; i < units.Count; i++)
            {
                var (start, length, _, unitScript) = units[i];
                var strongScript = unitScript != Script.Common && unitScript != Script.Inherited && unitScript != Script.Unknown;
                if (strongScript) inherited = unitScript;
                if (runs.Count > 0 && runs[^1].Level == levels[i]
                    && (!strongScript || runs[^1].Script == unitScript || runs[^1].Script == Script.Common))
                {
                    var last = runs[^1];
                    runs[^1] = last with { Length = last.Length + length, Script = strongScript ? unitScript : last.Script };
                    continue;
                }
                runs.Add(new Run(start, length, levels[i], strongScript ? unitScript : inherited));
            }

            Reorder(runs);
            return (runs, rightToLeft);
        }

        private static Class Classify(Rune rune, Script script)
        {
            if (script == Script.Common || script == Script.Inherited || script == Script.Unknown)
                return Rune.IsDigit(rune) ? Class.Number : Class.Neutral;
            return script.HorizontalDirection == Direction.RightToLeft ? Class.Right : Class.Left;
        }

        private static int[] Resolve(Class[] classes, bool rightToLeft)
        {
            var paragraph = rightToLeft ? Class.Right : Class.Left;
            var resolved = (Class[])classes.Clone();

            // A number after left-to-right text is part of it.
            var lastStrong = paragraph;
            for (var i = 0; i < resolved.Length; i++)
            {
                if (resolved[i] is Class.Left or Class.Right) lastStrong = resolved[i];
                else if (resolved[i] == Class.Number && lastStrong == Class.Left) resolved[i] = Class.Left;
            }

            // Neutrals join like neighbours and otherwise take the paragraph's direction; a number counts as right-to-left here.
            for (var i = 0; i < resolved.Length; i++)
            {
                if (resolved[i] != Class.Neutral) continue;
                var end = i;
                while (end < resolved.Length && resolved[end] == Class.Neutral) end++;
                var before = i > 0 ? Side(resolved[i - 1]) : paragraph;
                var after = end < resolved.Length ? Side(resolved[end]) : paragraph;
                var chosen = before == after ? before : paragraph;
                for (var n = i; n < end; n++) resolved[n] = chosen;
                i = end - 1;
            }

            return resolved.Select(c => c switch
            {
                Class.Right => 1,
                Class.Number => 2,
                _ => rightToLeft ? 2 : 0,
            }).ToArray();
        }

        private static Class Side(Class value) => value == Class.Number ? Class.Right : value;

        private static void Reorder(List<Run> runs)
        {
            if (runs.Count < 2) return;
            for (var level = runs.Max(r => r.Level); level >= 1; level--)
            {
                for (var i = 0; i < runs.Count; i++)
                {
                    if (runs[i].Level < level) continue;
                    var end = i;
                    while (end + 1 < runs.Count && runs[end + 1].Level >= level) end++;
                    runs.Reverse(i, end - i + 1);
                    i = end;
                }
            }
        }
    }
}
