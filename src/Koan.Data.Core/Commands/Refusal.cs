namespace Koan.Data.Core.Commands;

/// <summary>What code below a command throws to refuse, since it has no <see cref="Outcome{TResult}"/> of the command's to return.
/// It reaches the sender as <see cref="Outcome{TResult}.Refused"/>, and the command leaves nothing.</summary>
public sealed class Refusal(Reason reason) : Exception(reason.Text)
{
    public Reason Reason { get; } = reason;
}
