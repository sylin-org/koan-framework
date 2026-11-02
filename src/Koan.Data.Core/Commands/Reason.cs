namespace Koan.Data.Core.Commands;

/// <summary>
/// Why a command was refused, with its kind declared once, where the reason is defined (DATA-0167). A plain string is an invalid reason,
/// which is what most are, so <c>new Refusal(NoTopic)</c> reads the same whichever this constant is:
/// <code>public static readonly Reason NoTopic = Reason.Absent("There is no such Topic.");
/// public static readonly Reason TimedOut = Reason.Gated("timed-out", "You're timed out: you can read and report here, and post again once it ends.");</code>
/// </summary>
public sealed record Reason
{
    /// <summary>What every door says for anything absent, so that no wording tells a hidden resource from a missing one.</summary>
    private const string Nothing = "Nothing answers at this address.";

    private Reason(ReasonKind kind, string text, string? word = null, string? next = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        (Kind, Text, Word, Next) = (kind, text, word, next);
    }

    public ReasonKind Kind { get; }

    public string Text { get; }

    /// <summary>A gate's stable word, which a client or a mind acts on where the sentence is for reading.</summary>
    public string? Word { get; }

    /// <summary>A gate's next step, where it has one: the address that opens it.</summary>
    public string? Next { get; }

    /// <summary>What a surface says for this reason: its text, except that everything absent says one thing.</summary>
    public string Said => Kind == ReasonKind.Absent ? Nothing : Text;

    public static Reason Absent(string text) => new(ReasonKind.Absent, text);

    public static Reason Gated(string text) => new(ReasonKind.Gated, text);

    /// <summary>A gate with a stable word, its sentence, and the address of what one can do now where there is one.</summary>
    public static Reason Gated(string word, string text, string? next = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return new(ReasonKind.Gated, text, word, next);
    }

    public static Reason Conflict(string text) => new(ReasonKind.Conflict, text);

    public static implicit operator Reason(string text) => new(ReasonKind.Invalid, text);

    public override string ToString() => Text;
}
