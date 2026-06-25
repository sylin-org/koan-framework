using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Core.KeyValue;

/// <summary>
/// The hybrid, managed-aware filter evaluator for the <see cref="KeyValueStore{TEntity,TKey}"/> family (ARCH-0103 §9.2).
/// A <see cref="KvRecord{TEntity}"/> is the entity plus its stamped managed values. <see cref="InMemoryFilterEvaluator"/>
/// <b>refuses</b> managed fields by design (<c>ResolvedField.GetValue</c> throws "cannot be evaluated in memory"), so this
/// walker dispatches per filter leaf:
/// <list type="bullet">
/// <item>a <see cref="FieldFilter"/> whose single-segment path resolves to a managed field (<c>IsManaged</c>) is evaluated
/// by <see cref="DictionaryFilterEvaluator"/> over the record's <see cref="KvRecord{TEntity}.Managed"/> dictionary;</item>
/// <item>every other node (a POCO <see cref="FieldFilter"/>, a <see cref="ClrFilter"/>) is evaluated by
/// <see cref="InMemoryFilterEvaluator"/> over the <see cref="KvRecord{TEntity}.Entity"/>.</item>
/// </list>
/// Both convergence oracles are reused, neither is duplicated. <b>Off ⇒ byte-identical:</b> when no managed field is
/// registered (<c>ManagedFieldRegistry.IsEmpty</c>) no leaf is ever managed, so the whole filter routes to
/// <see cref="InMemoryFilterEvaluator"/> over the entity exactly as the pre-rebuild adapters did.
/// </summary>
internal static class KvFilterEvaluator
{
    private static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public static Func<KvRecord<TEntity>, bool> Compile<TEntity>(Filter filter) where TEntity : class
        => Build<TEntity>(filter);

    private static Func<KvRecord<TEntity>, bool> Build<TEntity>(Filter filter) where TEntity : class
    {
        switch (filter)
        {
            case AllOf all:
            {
                var ps = all.Operands.Select(Build<TEntity>).ToArray();
                return r => { foreach (var p in ps) if (!p(r)) return false; return true; };
            }
            case AnyOf any:
            {
                var ps = any.Operands.Select(Build<TEntity>).ToArray();
                return r => { foreach (var p in ps) if (p(r)) return true; return false; };
            }
            case Not n:
            {
                var inner = Build<TEntity>(n.Operand);
                return r => !inner(r);
            }
            case FieldFilter f when IsManaged<TEntity>(f.Field):
            {
                // Managed leaf — evaluate against the stamped sidecar/managed values, never the POCO.
                var pred = DictionaryFilterEvaluator.Compile(f);
                return r => pred(r.Managed ?? Empty);
            }
            default:
            {
                // POCO FieldFilter or ClrFilter — the entity is the evaluation target (InMemoryFilterEvaluator is the
                // CLR convergence oracle); compile this single node once and apply it to the entity.
                var pred = InMemoryFilterEvaluator.Compile<TEntity>(filter);
                return r => pred(r.Entity);
            }
        }
    }

    private static bool IsManaged<TEntity>(FieldPath field) where TEntity : class
    {
        if (ManagedFieldRegistry.IsEmpty || field.Segments.Count != 1) return false;
        return FieldPathResolver.Resolve(typeof(TEntity), field).IsManaged;
    }
}
