using Koan.Data.Abstractions.Lexical;

namespace Koan.Data.Core.Lexical;

/// <summary>
/// Compact declaration of one lexical search — the <c>s => s.Top(20).Snippet()</c> shape passed to
/// <see cref="LexicalStatics{T}.Search"/> and <see cref="LexicalStatics{T}.Find"/>. Fluent like
/// <c>SemanticSearchQuery</c>, scoped to the entity-level knobs. A search is bounded by construction: a declaration
/// that names neither <see cref="Top"/> nor <see cref="Page"/> is refused.
/// </summary>
public sealed class LexicalSearchQuery
{
    private int _page;
    private int _size;
    private LexicalSnippet? _snippet;

    /// <summary>The first <paramref name="size"/> matches, best first.</summary>
    public LexicalSearchQuery Top(int size) => Page(1, size);

    /// <summary>Page <paramref name="page"/> (from 1) of <paramref name="size"/> matches, best first.</summary>
    public LexicalSearchQuery Page(int page, int size)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page), page, "A lexical search page starts at 1.");
        if (size <= 0 || size > LexicalQuery.MaximumSize)
            throw new ArgumentOutOfRangeException(nameof(size), size,
                $"A lexical search page holds between 1 and {LexicalQuery.MaximumSize} matches.");
        _page = page;
        _size = size;
        return this;
    }

    /// <summary>Carry a snippet with each match: at most <paramref name="words"/> words around the matched terms, each term wrapped in the markers.</summary>
    public LexicalSearchQuery Snippet(int words = LexicalSnippet.DefaultWords, string open = "<mark>", string close = "</mark>")
    {
        _snippet = new LexicalSnippet(words, open, close);
        return this;
    }

    internal LexicalQuery Build(string text)
    {
        if (_size == 0)
            throw new ArgumentException(
                "A lexical search is bounded: declare Top(size) or Page(page, size) on the query.", nameof(text));
        return new LexicalQuery(text, _page, _size, _snippet);
    }
}
