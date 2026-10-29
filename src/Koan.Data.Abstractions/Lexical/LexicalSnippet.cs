namespace Koan.Data.Abstractions.Lexical;

/// <summary>
/// How a match's snippet is cut from the indexed text and how the matched terms are marked inside it.
/// </summary>
/// <param name="Words">The window: at most this many words around the matched terms, between 1 and <see cref="MaximumWords"/>.</param>
/// <param name="Open">The text placed before each matched term.</param>
/// <param name="Close">The text placed after each matched term.</param>
public sealed record LexicalSnippet(int Words = LexicalSnippet.DefaultWords, string Open = "<mark>", string Close = "</mark>")
{
    /// <summary>The window used when a caller asks for a snippet without naming one.</summary>
    public const int DefaultWords = 12;

    /// <summary>The widest window a store cuts; SQLite's FTS5 refuses a wider one.</summary>
    public const int MaximumWords = 64;

    /// <summary>The window: at most this many words around the matched terms.</summary>
    public int Words { get; } = Words is > 0 and <= MaximumWords
        ? Words
        : throw new ArgumentOutOfRangeException(nameof(Words), Words, $"A snippet window holds between 1 and {MaximumWords} words.");

    /// <summary>The text placed before each matched term.</summary>
    public string Open { get; } = Open ?? throw new ArgumentNullException(nameof(Open));

    /// <summary>The text placed after each matched term.</summary>
    public string Close { get; } = Close ?? throw new ArgumentNullException(nameof(Close));
}
