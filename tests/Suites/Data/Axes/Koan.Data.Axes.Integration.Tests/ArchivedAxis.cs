using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Axes;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// A faithful soft-delete clone authored ENTIRELY through the <see cref="IDataAxis"/> premium layer — the D3 oracle.
/// It declares the SAME planes the hand-written <c>Koan.Data.SoftDelete</c> registrar registers by hand (an invisible
/// <c>__archived</c> managed field, a NULL-safe hide-archived read predicate, and a <c>Delete ⇒ __archived = true</c>
/// override), so a <c>[Archived]</c> entity must behave byte-identically to a <c>[SoftDelete]</c> one. Discovered +
/// expanded at every <c>AddKoan()</c> boot in this assembly; AppliesTo-gated to <c>[Archived]</c> so every other entity
/// is byte-identical (the axis is a no-op for them).
/// </summary>
public sealed class ArchivedAxis : IDataAxis
{
    // visible ⇔ __archived absent (IS NULL) OR __archived <> true (NULL-safe, like SoftDeleteReadContributor).
    private static readonly Filter HideArchived = Filter.Any(
        Filter.On(FieldPath.Of("__archived"), FilterOperator.Exists, FilterValue.Of(false)),
        Filter.On(FieldPath.Of("__archived"), FilterOperator.Ne, FilterValue.Of(true)));

    public void Declare(Axis axis) => axis
        .Named("archived")
        .AppliesTo(ArchivedMetadata.IsArchived)
        .Field("__archived", static () => null, typeof(bool))                       // absent on normal writes; stamped by OnDelete
        .Reads(static _ => ArchivedAmbient.ShowArchived ? null : HideArchived)       // hide-archived unless the recycle bin is open
        .OnDelete(Logical.SetTrue("__archived"));                                    // Delete ⇒ __archived = true
}

/// <summary>Per-entity opt-in (the soft-delete <c>[SoftDelete]</c> analogue).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ArchivedAttribute : Attribute;

internal static class ArchivedMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsArchived(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<ArchivedAttribute>(inherit: true) is not null);
}

/// <summary>The ambient "show archived rows" scope (the <c>SoftDeleteAmbient</c> analogue).</summary>
internal static class ArchivedAmbient
{
    private static readonly AsyncLocal<bool> _show = new();
    public static bool ShowArchived => _show.Value;
    public static IDisposable Enter()
    {
        var prev = _show.Value;
        _show.Value = true;
        return new Scope(prev);
    }
    private sealed class Scope(bool previous) : IDisposable
    {
        private bool _done;
        public void Dispose() { if (_done) return; _done = true; _show.Value = previous; }
    }
}

/// <summary>The recycle-bin scope verb: <c>using (Archived.WithDeleted()) { ... }</c> includes archived rows.</summary>
public static class Archived
{
    public static IDisposable WithDeleted() => ArchivedAmbient.Enter();
}
