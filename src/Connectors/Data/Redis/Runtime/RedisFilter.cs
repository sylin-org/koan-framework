using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Connector.Redis.Runtime;

internal static class RedisFilter
{
    private static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    internal static Func<RedisRecord<TEntity>, bool> Compile<TEntity>(Filter filter) where TEntity : class =>
        Build<TEntity>(filter);

    private static Func<RedisRecord<TEntity>, bool> Build<TEntity>(Filter filter) where TEntity : class => filter switch
    {
        AllOf all => All(all.Operands.Select(Build<TEntity>).ToArray()),
        AnyOf any => Any(any.Operands.Select(Build<TEntity>).ToArray()),
        Not not => Negate(Build<TEntity>(not.Operand)),
        FieldFilter field when Managed<TEntity>(field.Field) => ManagedPredicate<TEntity>(field),
        _ => EntityPredicate<TEntity>(filter)
    };

    private static Func<RedisRecord<TEntity>, bool> All<TEntity>(Func<RedisRecord<TEntity>, bool>[] predicates) =>
        record => predicates.All(predicate => predicate(record));

    private static Func<RedisRecord<TEntity>, bool> Any<TEntity>(Func<RedisRecord<TEntity>, bool>[] predicates) =>
        record => predicates.Any(predicate => predicate(record));

    private static Func<RedisRecord<TEntity>, bool> Negate<TEntity>(Func<RedisRecord<TEntity>, bool> predicate) =>
        record => !predicate(record);

    private static Func<RedisRecord<TEntity>, bool> ManagedPredicate<TEntity>(FieldFilter field)
    {
        var predicate = DictionaryFilterEvaluator.Compile(field);
        return record => predicate(record.Managed ?? Empty);
    }

    private static Func<RedisRecord<TEntity>, bool> EntityPredicate<TEntity>(Filter filter)
    {
        var predicate = InMemoryFilterEvaluator.Compile<TEntity>(filter);
        return record => predicate(record.Entity);
    }

    private static bool Managed<TEntity>(FieldPath path)
    {
        if (path.Segments.Count != 1) return false;
        if (path.ManagedClrType is null && ManagedFieldRegistry.IsEmpty) return false;
        return FieldPathResolver.Resolve(typeof(TEntity), path).IsManaged;
    }
}
