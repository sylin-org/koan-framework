namespace Koan.Data.Core.Commands;

/// <summary>
/// Why a command was refused: its kind, its sentence and, for a gate, a stable word (ARCH-0143). The kind is declared once, where the
/// reason is defined (DATA-0167), and equality is the record's:
/// <code>public static readonly Reason NoBody = Reason.Invalid("A reply needs a body.");
/// public static readonly Reason TimedOut = Reason.Gated("timed-out", "You're timed out: you can read and report here, and post again once it ends.");</code>
/// What a surface shows around it, a heading or the address to go to next, is the surface's.
/// </summary>
public sealed record Reason
{
    private Reason(ReasonKind kind, string text, string? word = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        (Kind, Text, Word) = (kind, text, word);
    }

    public ReasonKind Kind { get; }

    public string Text { get; }

    /// <summary>A gate's stable word, which a client or a mind acts on where the sentence is for reading.</summary>
    public string? Word { get; }

    public static Reason Invalid(string text) => new(ReasonKind.Invalid, text);

    public static Reason Absent(string text) => new(ReasonKind.Absent, text);

    public static Reason Gated(string text) => new(ReasonKind.Gated, text);

    /// <summary>A gate with a stable word and its sentence.</summary>
    public static Reason Gated(string word, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return new(ReasonKind.Gated, text, word);
    }

    public static Reason Conflict(string text) => new(ReasonKind.Conflict, text);

    public override string ToString() => Text;
}
