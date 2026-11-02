namespace Koan.Data.Core.Commands;

/// <summary>
/// Why a command was refused, with its kind declared once, where the reason is defined (DATA-0167). A plain string is an invalid reason,
/// which is what most are, so <c>new Refusal(NoTopic)</c> reads the same whichever this constant is:
/// <code>public static readonly Reason NoTopic = Reason.Absent("There is no such Topic.");</code>
/// </summary>
public sealed record Reason
{
    private Reason(ReasonKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        (Kind, Text) = (kind, text);
    }

    public ReasonKind Kind { get; }

    public string Text { get; }

    public static Reason Absent(string text) => new(ReasonKind.Absent, text);

    public static Reason Gated(string text) => new(ReasonKind.Gated, text);

    public static Reason Conflict(string text) => new(ReasonKind.Conflict, text);

    public static implicit operator Reason(string text) => new(ReasonKind.Invalid, text);

    public override string ToString() => Text;
}
