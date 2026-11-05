namespace Koan.Data.Core.Commands;

/// <summary>
/// Why a command was refused: its kind, its sentence, its word and, for a value it could not take, the wire field it concerns
/// (ARCH-0143, ARCH-0145). The kind is declared once, where the reason is defined (DATA-0167), and equality is the record's:
/// <code>public static readonly Reason NoBody = Reason.Invalid("body", "A reply needs a body.");
/// public static readonly Reason TimedOut = Reason.Gated("timed-out", "You're timed out: you can read and report here, and post again once it ends.");
/// public static readonly Reason Taken = Reason.Conflict("id-taken", "Another Post already has this Id.");</code>
/// What a surface shows around it, a heading or the address to go to next, is the surface's.
/// </summary>
public sealed record Reason
{
    private Reason(ReasonKind kind, string text, string? word = null, string? field = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        (Kind, Text, Word, Field) = (kind, text, word ?? WordOf(kind), field);
    }

    public ReasonKind Kind { get; }

    public string Text { get; }

    /// <summary>The stable word a client or a mind acts on, where the sentence is for reading: the one the reason names, and its kind's
    /// (<c>invalid</c>, <c>absent</c>, <c>gated</c>, <c>conflict</c>) where it names none.</summary>
    public string Word { get; }

    /// <summary>The wire field an invalid reason concerns, as a client sends it; null where it concerns what was sent as a whole.</summary>
    public string? Field { get; }

    public static Reason Invalid(string text) => new(ReasonKind.Invalid, text);

    /// <summary>A value the sender gave in <paramref name="field"/>, the name it travels under, cannot be taken.</summary>
    public static Reason Invalid(string field, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new(ReasonKind.Invalid, text, field: field);
    }

    public static Reason Absent(string text) => new(ReasonKind.Absent, text);

    public static Reason Gated(string text) => new(ReasonKind.Gated, text);

    /// <summary>A gate with its own word and its sentence.</summary>
    public static Reason Gated(string word, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return new(ReasonKind.Gated, text, word);
    }

    public static Reason Conflict(string text) => new(ReasonKind.Conflict, text);

    /// <summary>A conflict a client must tell apart from another, by its own word.</summary>
    public static Reason Conflict(string word, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return new(ReasonKind.Conflict, text, word);
    }

    public override string ToString() => Text;

    private static string WordOf(ReasonKind kind) => kind switch
    {
        ReasonKind.Invalid => "invalid",
        ReasonKind.Absent => "absent",
        ReasonKind.Gated => "gated",
        ReasonKind.Conflict => "conflict",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A reason is one of four kinds."),
    };
}
