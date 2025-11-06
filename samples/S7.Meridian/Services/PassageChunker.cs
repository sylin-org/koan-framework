using System.Text.RegularExpressions;
using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.Services;

public interface IPassageChunker
{
    List<Passage> Chunk(SourceDocument document, string text);
}

public sealed class PassageChunker : IPassageChunker
{
    private static readonly Regex SectionRegex = new("^#+\\s*(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);

    public List<Passage> Chunk(SourceDocument document, string text)
    {
        var passages = new List<Passage>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return passages;
        }

        var blocks = SplitIntoBlocks(text);
        var sequence = 0;
        foreach (var block in blocks)
        {
            var normalized = block.Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            var passage = new Passage
            {
                SourceDocumentId = document.Id,
                SequenceNumber = sequence++,
                Text = normalized,
                TextHash = TextExtractor.ComputeTextHash(normalized),
                Section = ResolveSection(block)
            };

            passages.Add(passage);
        }

        return passages;
    }

    private static IEnumerable<string> SplitIntoBlocks(string text)
    {
        var builder = new List<string>();
        using var reader = new StringReader(text);
        var current = new List<string>();

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Count > 0)
                {
                    builder.Add(string.Join(Environment.NewLine, current));
                    current.Clear();
                }
                continue;
            }

            current.Add(line);
            if (current.Sum(l => l.Length) > 1000)
            {
                builder.Add(string.Join(Environment.NewLine, current));
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            builder.Add(string.Join(Environment.NewLine, current));
        }

        return builder;
    }

    private static string? ResolveSection(string block)
    {
        var match = SectionRegex.Match(block);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
