namespace Koan.Data.Abstractions.Lexical;

/// <summary>One ranked hit of a lexical search.</summary>
/// <param name="Id">The matched entity's identity.</param>
/// <param name="Score">
/// The store's relevance for this match, higher first. Scores compare within one answer and one store; SQLite reports
/// the negated BM25 rank and PostgreSQL its cover-density rank, so a score is never compared across stores or queries.
/// </param>
/// <param name="Snippet">The marked excerpt around the matched terms, when the query asked for one.</param>
public sealed record LexicalMatch<TKey>(TKey Id, double Score, string? Snippet) where TKey : notnull;
