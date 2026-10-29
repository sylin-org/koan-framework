namespace Koan.Data.Abstractions.Lexical;

/// <summary>
/// One bounded term search over an entity's lexical index: the text to find, the page of ranked matches to answer,
/// and whether each match carries a snippet.
///
/// <para>Every term is optional and a row holding more of them ranks higher, so a two-word query answers the rows
/// holding both words before the rows holding one. The terms are the maximal letter-or-digit runs of the text,
/// compared without case; punctuation and query operators are not part of the grammar, so a store never sees text it
/// could misparse. A query is bounded by construction: a page has a size, the size has a ceiling, and a snippet has a
/// window.</para>
/// </summary>
public sealed record LexicalQuery
{
    /// <summary>The largest page a search answers. A larger page is a scan with a bound in name only.</summary>
    public const int MaximumSize = 1_000;

    public LexicalQuery(string text, int page, int size, LexicalSnippet? snippet = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page), page, "A lexical search page starts at 1.");
        if (size <= 0 || size > MaximumSize)
            throw new ArgumentOutOfRangeException(nameof(size), size,
                $"A lexical search page holds between 1 and {MaximumSize} matches.");
        Terms = SplitTerms(text);
        if (Terms.Count == 0)
            throw new ArgumentException("A lexical search needs at least one term made of letters or digits.", nameof(text));
        Text = text;
        Page = page;
        Size = size;
        Snippet = snippet;
    }

    /// <summary>The text as the caller gave it.</summary>
    public string Text { get; }

    /// <summary>The distinct terms the store matches, in the order they first appear.</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>The page of ranked matches to answer, starting at 1.</summary>
    public int Page { get; }

    /// <summary>How many matches one page holds.</summary>
    public int Size { get; }

    /// <summary>How a snippet is cut and marked, or <see langword="null"/> for matches without one.</summary>
    public LexicalSnippet? Snippet { get; }

    /// <summary>How many ranked matches precede this page.</summary>
    public int Offset => checked((Page - 1) * Size);

    private static IReadOnlyList<string> SplitTerms(string text)
    {
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var inTerm = index < text.Length && char.IsLetterOrDigit(text[index]);
            if (inTerm && start < 0) start = index;
            if (inTerm || start < 0) continue;
            var term = text[start..index];
            start = -1;
            if (seen.Add(term)) terms.Add(term);
        }
        return terms;
    }
}
