using System.Collections;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Direct;

/// <summary>
/// The rows a Direct query returned, and whether they are all of them. A Direct query reads at most its session's
/// <c>MaxRows</c>; when the command had more, <see cref="Completion"/> is <see cref="RecordSetCompletion.RecordLimit"/>
/// and the rows are the first ones the command produced, so a cut answer always says it was cut (DATA-0162). Every path
/// reads at most one row past <c>MaxRows</c>, the entity-routed one included: its adapter is asked for that many rows,
/// and every relational adapter Koan ships stops there. A declared Read outside a caller's transaction then cancels its
/// command, on every path, so a driver that drains on close (Npgsql, SqlClient) does not read the rest of the result;
/// any other command drains to its end, so a write it carries completes (<see cref="BoundedReads"/>).
/// </summary>
public sealed class DirectRows<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> _rows;

    public DirectRows(IReadOnlyList<T> rows, RecordSetCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;
        Completion = completion;
    }

    /// <summary>Why reading stopped: <see cref="RecordSetCompletion.Complete"/> when every row was read.</summary>
    public RecordSetCompletion Completion { get; }

    /// <summary>Whether these are all the rows the command produced.</summary>
    public bool IsComplete => Completion == RecordSetCompletion.Complete;

    public int Count => _rows.Count;

    public T this[int index] => _rows[index];

    public IEnumerator<T> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
