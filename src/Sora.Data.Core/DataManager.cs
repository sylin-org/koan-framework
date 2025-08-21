using Sora.Core.Primitives;
using Sora.Data.Abstractions;
using Sora.Data.Core.Metadata;
using System.Reflection;

namespace Sora.Data.Core;

public interface IAggregateIdentityManager
{
    ValueTask EnsureIdAsync<TEntity, TKey>(TEntity entity, CancellationToken ct = default)
    where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}

public sealed class AggregateIdentityManager : IAggregateIdentityManager
{
    private static (PropertyInfo? Prop, bool IsString, bool IsGuid) GetIdProp<TEntity>()
    {
        var spec = AggregateMetadata.GetIdSpec(typeof(TEntity));
        return (spec?.Prop, spec?.IsString == true, spec?.IsGuid == true);
    }

    public ValueTask EnsureIdAsync<TEntity, TKey>(TEntity entity, CancellationToken ct = default)
    where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (entity is null) return ValueTask.CompletedTask;
        var (prop, isString, isGuid) = GetIdProp<TEntity>();
        if (prop is null) return ValueTask.CompletedTask;

        object? current = prop.GetValue(entity);
        if (isString)
        {
            var str = current as string;
            if (string.IsNullOrWhiteSpace(str) && prop.CanWrite)
                prop.SetValue(entity, StringId.New());
        }
        else if (isGuid)
        {
            if ((current is Guid g && g == default) && prop.CanWrite)
                prop.SetValue(entity, Guid.NewGuid());
        }
        return ValueTask.CompletedTask;
    }
}
