using System.Linq.Expressions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Lexical;

namespace Koan.Data.Core.Lexical;

/// <summary>
/// Compact declaration of one lexical search — the <c>s => s.Top(20).Snippet()</c> shape passed to
/// <see cref="LexicalStatics{T}.Search"/> and <see cref="LexicalStatics{T}.Find"/>. Fluent like
/// <c>SemanticSearchQuery</c>, scoped to the entity-level knobs. A search is bounded by construction: a declaration
/// that names neither <see cref="Top"/> nor <see cref="Page"/> is refused, and a page is refused here, before any
/// store runs it, when its size is over <see cref="LexicalQuery.MaximumSize"/> or it starts deeper than
/// <see cref="LexicalQuery.MaximumOffset"/> matches.
/// </summary>
public sealed class LexicalSearchQuery<TEntity>
{
    private int _page;
    private int _size;
    private LexicalSnippet? _snippet;
    private bool _typed;
    private bool _oneMore;

    /// <summary>The first <paramref name="size"/> matches, best first.</summary>
    public LexicalSearchQuery<TEntity> Top(int size) => Page(1, size);

    /// <summary>Page <paramref name="page"/> (from 1) of <paramref name="size"/> matches, best first.</summary>
    public LexicalSearchQuery<TEntity> Page(int page, int size)
    {
        LexicalQuery.RequirePage(page, size);
        _page = page;
        _size = size;
        return this;
    }

    /// <summary>
    /// Answer one match past the page as well, so this one search says whether a next page holds any: more matches than the page's size
    /// means it does, and the page is the first of them.
    /// </summary>
    public LexicalSearchQuery<TEntity> OneMore()
    {
        _oneMore = true;
        return this;
    }

    /// <summary>Carry a snippet with each match: at most <paramref name="words"/> words around the matched terms, each term wrapped in the markers.</summary>
    public LexicalSearchQuery<TEntity> Snippet(int words = LexicalSnippet.DefaultWords, string open = "<mark>", string close = "</mark>")
    {
        _snippet = new LexicalSnippet(words, open, close);
        return this;
    }

    /// <summary>
    /// Read the text as someone still typing it: the word it ends in also matches as the start of a word, so "lant"
    /// finds "lantern". Text that ends in a space or punctuation holds no such word. See <see cref="LexicalQuery.Typing"/>
    /// for what a stemming language does to a partial word.
    /// </summary>
    public LexicalSearchQuery<TEntity> AsTyped()
    {
        _typed = true;
        return this;
    }

    /// <summary>
    /// Search only the rows the predicate holds for: the store applies it before it ranks and pages, so a page is full
    /// where such rows remain and says nothing of any other row. Named twice, a row answers both. The predicate runs in
    /// the store; one the store cannot run is refused at the search, never applied after it.
    /// </summary>
    public LexicalSearchQuery<TEntity> Within(Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Scope = Filter.And(Scope, LinqFilterCompiler.Compile(predicate));
        return this;
    }

    internal Filter? Scope { get; private set; }

    internal LexicalQuery Build(string text)
    {
        if (_size == 0)
            throw new ArgumentException(
                "A lexical search is bounded: declare Top(size) or Page(page, size) on the query.", nameof(text));
        return new LexicalQuery(text, _page, _size, _snippet, _typed, _oneMore);
    }
}
