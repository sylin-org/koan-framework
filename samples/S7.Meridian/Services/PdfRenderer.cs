using System.Text;

namespace Koan.Samples.Meridian.Services;

public interface IPdfRenderer
{
    Task<byte[]> RenderAsync(string markdown, CancellationToken ct = default);
}

public sealed class PdfRenderer : IPdfRenderer
{
    public Task<byte[]> RenderAsync(string markdown, CancellationToken ct = default)
    {
        var lines = (markdown ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        var objects = new List<string>();
        var offsets = new List<int> { 0 };
        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");

        // 1: Catalog
        objects.Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        // 2: Pages
        objects.Add("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        // 3: Page
        objects.Add("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        var content = BuildContentStream(lines);
        var contentBytes = Encoding.ASCII.GetBytes(content);
        objects.Add($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{content}endstream\nendobj\n");

        // 5: Font
        objects.Add("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        foreach (var obj in objects)
        {
            offsets.Add(sb.Length);
            sb.Append(obj);
        }

        var xrefOffset = sb.Length;
        sb.AppendLine("xref");
        sb.AppendLine($"0 {objects.Count + 1}");
        sb.AppendLine("0000000000 65535 f ");
        for (var i = 1; i < offsets.Count; i++)
        {
            sb.AppendLine($"{offsets[i]:D10} 00000 n ");
        }

        sb.AppendLine("trailer");
        sb.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        sb.AppendLine("startxref");
        sb.AppendLine(xrefOffset.ToString());
        sb.AppendLine("%%EOF");

        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        return Task.FromResult(bytes);
    }

    private static string BuildContentStream(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BT");
        builder.AppendLine("/F1 12 Tf");
        builder.AppendLine("14 TL");
        builder.AppendLine("50 770 Td");

        var first = true;
        foreach (var raw in lines)
        {
            var line = EscapePdfText(raw ?? string.Empty);
            if (!first)
            {
                builder.AppendLine("T*");
            }
            builder.AppendLine($"({line}) Tj");
            first = false;
        }

        builder.AppendLine("ET");
        builder.Append('\n');
        return builder.ToString();
    }

    private static string EscapePdfText(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }
}
