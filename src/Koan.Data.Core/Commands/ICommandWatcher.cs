namespace Koan.Data.Core.Commands;

/// <summary>Registered in DI, and told in this process of each command that committed a write, in commit order, before the store's turn
/// passes on. So it returns at once. It runs in the sender's context with the hold no longer ambient, so anything it starts can send, and
/// takes its turn behind this command.</summary>
public interface ICommandWatcher
{
    /// <summary>The command, and the last number it took from each sequence. What this throws is logged and costs no other watcher.</summary>
    void Committed(object command, IReadOnlyDictionary<Sequence, long> sequences);
}
