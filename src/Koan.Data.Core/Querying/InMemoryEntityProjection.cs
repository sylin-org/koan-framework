using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Querying;

/// <summary>
/// Compiles the correctness fallback for Entity sparse projections. Plans are pure type structure,
/// weakly rooted by the CLR type, and bounded per type; applying a warm plan uses no reflection or JSON.
/// </summary>
internal static class InMemoryEntityProjection
{
    private const int MaxPlansPerType = 256;
    private static readonly ConditionalWeakTable<Type, TypePlans> Plans = new();

    public static void Validate(Type entityType, Projection? projection)
    {
        if (projection is null) return;
        _ = Plans.GetValue(entityType, static _ => new TypePlans()).GetOrAdd(entityType, projection);
    }

    public static IReadOnlyList<TEntity> Apply<TEntity>(IReadOnlyList<TEntity> source, Projection projection)
    {
        if (source.Count == 0) return source;
        var plan = (ProjectionPlan<TEntity>)Plans
            .GetValue(typeof(TEntity), static _ => new TypePlans())
            .GetOrAdd(typeof(TEntity), projection);
        var projected = new TEntity[source.Count];
        for (var i = 0; i < source.Count; i++) projected[i] = plan.Apply(source[i]);
        return projected;
    }

    private sealed class TypePlans
    {
        private readonly Dictionary<string, LinkedListNode<Entry>> _plans = new(StringComparer.Ordinal);
        private readonly LinkedList<Entry> _lru = new();
        private readonly object _gate = new();

        public object GetOrAdd(Type entityType, Projection projection)
        {
            var key = Normalize(entityType, projection);
            lock (_gate)
            {
                if (_plans.TryGetValue(key, out var existing))
                {
                    _lru.Remove(existing);
                    _lru.AddFirst(existing);
                    return existing.Value.Plan;
                }

                var plan = Build(entityType, key.Split('\u001f', StringSplitOptions.RemoveEmptyEntries));
                var node = new LinkedListNode<Entry>(new Entry(key, plan));
                _lru.AddFirst(node);
                _plans.Add(key, node);
                if (_plans.Count > MaxPlansPerType)
                {
                    var last = _lru.Last!;
                    _lru.RemoveLast();
                    _plans.Remove(last.Value.Key);
                }
                return plan;
            }
        }

        private static string Normalize(Type entityType, Projection projection)
        {
            if (projection.Fields is null)
                throw ProjectionFailure(entityType, "Projection fields cannot be null.");

            var fields = projection.Fields
                .Select(field => field?.Trim())
                .ToArray();
            if (fields.Any(string.IsNullOrWhiteSpace))
                throw ProjectionFailure(entityType, "Projection fields must be non-empty property names.");
            if (fields.Any(field => field!.Contains('.', StringComparison.Ordinal)))
                throw ProjectionFailure(entityType,
                    "Nested sparse Entity projection is not representable by the current Entity result. Use a flat Entity projection or a registered RecordSet query.");

            return string.Join('\u001f', fields!
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase));
        }

        private static object Build(Type entityType, IReadOnlyList<string> selectedNames)
        {
            var method = typeof(TypePlans).GetMethod(nameof(BuildTyped), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType);
            try
            {
                return method.Invoke(null, [selectedNames])!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }

        private static ProjectionPlan<TEntity> BuildTyped<TEntity>(IReadOnlyList<string> selectedNames)
        {
            var entityType = typeof(TEntity);
            var properties = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
                .ToArray();
            var selected = new HashSet<PropertyInfo>();
            foreach (var name in selectedNames)
            {
                var matches = properties.Where(property =>
                    string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1)
                    throw ProjectionFailure(entityType,
                        matches.Length == 0
                            ? $"Projection property '{name}' does not exist."
                            : $"Projection property '{name}' is ambiguous by case.");
                selected.Add(matches[0]);
            }

            var id = AggregateMetadata.GetIdSpec(entityType)?.Prop;
            if (id is not null) selected.Add(id);

            var source = Expression.Parameter(entityType, "source");
            var memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var clone = Expression.Lambda<Func<TEntity, TEntity>>(
                Expression.Convert(Expression.Call(source, memberwiseClone), entityType), source).Compile();

            var target = Expression.Parameter(entityType, "target");
            var clears = properties
                .Where(property => property.SetMethod is not null && !selected.Contains(property))
                .Select(property => (Expression)Expression.Assign(
                    Expression.Property(target, property), Expression.Default(property.PropertyType)))
                .ToArray();
            var clear = clears.Length == 0
                ? new Action<TEntity>(static _ => { })
                : Expression.Lambda<Action<TEntity>>(Expression.Block(clears), target).Compile();

            return new ProjectionPlan<TEntity>(clone, clear);
        }

        private sealed record Entry(string Key, object Plan);
    }

    private sealed record ProjectionPlan<TEntity>(Func<TEntity, TEntity> CloneEntity, Action<TEntity> Clear)
    {
        public TEntity Apply(TEntity source)
        {
            var target = CloneEntity(source);
            Clear(target);
            return target;
        }
    }

    private static QueryReceiptRejectedException ProjectionFailure(Type entityType, string correction)
        => new(entityType.FullName ?? entityType.Name, QueryReceiptAxis.Projection, correction);
}
