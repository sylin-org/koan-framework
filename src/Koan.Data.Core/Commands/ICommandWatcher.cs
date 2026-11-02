namespace Koan.Data.Core.Commands;

/// <summary>Registered in DI, and told in this process of each command that committed a write, in commit order, before the store's turn
/// passes on. So it returns at once, and starts a task for anything else, which does not inherit the hold and can send.</summary>
public interface ICommandWatcher
{
    /// <summary>The command, and the last number it took from each sequence. What this throws is logged and costs no other watcher.</summary>
    void Committed(object command, IReadOnlyDictionary<Sequence, long> sequences);
}
