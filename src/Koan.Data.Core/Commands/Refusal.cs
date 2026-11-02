namespace Koan.Data.Core.Commands;

/// <summary>What code below a command throws to refuse, since it has no <see cref="Outcome{TResult}"/> of the command's to return.
/// It reaches the sender as that outcome, and the command leaves nothing.</summary>
public sealed class Refusal : Exception
{
    private enum Kind { Absent, Gated, Invalid, Conflict }

    private readonly Kind _kind;
    private readonly string _field;

    private Refusal(Kind kind, string message, string field = "") : base(message) => (_kind, _field) = (kind, field);

    public static Refusal Absent() => new(Kind.Absent, "What this acts on is absent.");

    public static Refusal Gated(string why) => new(Kind.Gated, why);

    public static Refusal Invalid(string field, string reason) => new(Kind.Invalid, reason, field);

    public static Refusal Conflict(string reason) => new(Kind.Conflict, reason);

    internal Outcome<TResult> As<TResult>() => _kind switch
    {
        Kind.Absent => new Outcome<TResult>.Absent(),
        Kind.Gated => new Outcome<TResult>.Gated(Message),
        Kind.Invalid => new Outcome<TResult>.Invalid(_field, Message),
        _ => new Outcome<TResult>.Conflict(Message),
    };
}
