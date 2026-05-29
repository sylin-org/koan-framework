using System;

namespace Koan.Jobs.Model;

/// <summary>
/// A reference to a job by its concrete type and id (JOBS-0003). Because jobs are stored
/// table-per-type, a bare id no longer implies a collection; a <see cref="JobRef"/> carries the
/// type so WaitFor, cancel, and drill-in can resolve the right set. The stored form keeps the
/// type's full name; <see cref="For{T}"/> is the typed constructor.
/// </summary>
public readonly record struct JobRef(string TypeName, string Id)
{
    public static JobRef For<T>(string id) where T : Job<T>, new()
        => new(typeof(T).FullName ?? typeof(T).Name, id);

    public bool IsType<T>() where T : Job<T>, new()
        => string.Equals(TypeName, typeof(T).FullName ?? typeof(T).Name, StringComparison.Ordinal);
}
