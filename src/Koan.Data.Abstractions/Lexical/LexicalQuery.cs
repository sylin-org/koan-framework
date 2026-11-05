namespace Koan.Data.Abstractions.Lexical;

/// <summary>
/// One bounded term search over an entity's lexical index: the text to find, the page of ranked matches to answer,
/// and whether each match carries a snippet.
///
/// <para>Every term is optional and a row holding more of them ranks higher, so a two-word query answers the rows
/// holding both words before the rows holding one. The terms are the maximal letter-or-digit runs of the text,
/// compared without case; punctuation and query operators are not part of the grammar, so a store never sees text it
/// could misparse. A query is bounded by construction: a page has a size, the size has a ceiling, a page starts no
/// deeper than <see cref="MaximumOffset"/> matches into the ranking, and a snippet has a window.</para>
/// </summary>
public sealed record LexicalQuery
{
    /// <summary>The largest page a search answers. A larger page is a scan with a bound in name only.</summary>
    public const int MaximumSize = 1_000;

    /// <summary>
    /// The deepest point a page may start at: how many ranked matches may precede it. A store ranks every match
    /// and discards the ones before the page, so a page that starts past this point is a scan the caller cannot
    /// read anyway; it is refused when the query is built, not when the store runs it.
    /// </summary>
    public const int MaximumOffset = 10_000;

    public LexicalQuery(string text, int page, int size, LexicalSnippet? snippet = null, bool typed = false, bool oneMore = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Offset = RequirePage(page, size);
        Terms = SplitTerms(text);
        if (Terms.Count == 0)
            throw new ArgumentException("A lexical search needs at least one term made of letters or digits.", nameof(text));
        Text = text;
        Page = page;
        Size = size;
        Snippet = snippet;
        Typing = typed ? TermBeingTyped(text) : null;
        OneMore = oneMore;
    }

    /// <summary>
    /// Check one page declaration and answer its offset: the page starts at 1, the size is between 1 and
    /// <see cref="MaximumSize"/>, and the matches before the page number at most <see cref="MaximumOffset"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The page or the size is outside those bounds.</exception>
    public static int RequirePage(int page, int size)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page), page, "A lexical search page starts at 1.");
        if (size <= 0 || size > MaximumSize)
            throw new ArgumentOutOfRangeException(nameof(size), size,
                $"A lexical search page holds between 1 and {MaximumSize} matches.");
        var offset = (long)(page - 1) * size;
        if (offset > MaximumOffset)
            throw new ArgumentOutOfRangeException(nameof(page), page,
                $"A lexical search page starts at most {MaximumOffset} matches deep; page {page} of {size} would skip {offset}. " +
                $"Ask for a page no later than {MaximumOffset / size + 1} at this size, or narrow the search.");
        return (int)offset;
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

    /// <summary>How many ranked matches precede this page; at most <see cref="MaximumOffset"/>.</summary>
    public int Offset { get; }

    /// <summary>Whether the store answers one match past the page, so the one search says whether a next page holds any.</summary>
    public bool OneMore { get; }

    /// <summary>How many matches the store answers at most, from <see cref="Offset"/>: the page's, and one more where it asks.</summary>
    public int Limit => OneMore ? Size + 1 : Size;

    /// <summary>
    /// The word someone is still typing, which the store matches as the start of a word: the term the text ends in, on
    /// a query declared as typed. <see langword="null"/> on any other query, and where the text ends in anything but a
    /// letter or digit, since the word before it is finished. The store's language still applies, so a partial word is
    /// stemmed before it is compared: where its stem is not the start of the stored one ("runnin" against "running",
    /// stored as "run") it finds nothing until the word is whole.
    /// </summary>
    public string? Typing { get; }

    /// <summary>Whether <paramref name="term"/>, one of <see cref="Terms"/>, is the word still being typed.</summary>
    public bool IsTyping(string term) => Typing is not null && string.Equals(term, Typing, StringComparison.OrdinalIgnoreCase);

    private static string? TermBeingTyped(string text)
    {
        var start = text.Length;
        while (start > 0 && char.IsLetterOrDigit(text[start - 1])) start--;
        return start == text.Length ? null : text[start..];
    }

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
