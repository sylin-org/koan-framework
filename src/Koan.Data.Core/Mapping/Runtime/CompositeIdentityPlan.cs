using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Mapping.Runtime;

internal sealed class CompositeIdentityPlan
{
    private readonly MappingMemberAccess _entityKey;
    private readonly MappingMemberAccess[] _components;
    private readonly Func<object?[], object> _construct;

    public CompositeIdentityPlan(
        MappingMemberAccess entityKey,
        MappingMemberAccess[] components,
        MappingBindingPlan[] bindings,
        Type keyType)
    {
        _entityKey = entityKey;
        _components = components;
        Bindings = bindings;
        _construct = CompileConstructor(keyType, components);
    }

    public MappingBindingPlan[] Bindings { get; }

    public object? ReadComponent(object entity, int index)
    {
        var key = _entityKey.Get(entity);
        return key is null ? null : _components[index].Get(key);
    }

    public void Assign(object entity, object?[] values) => _entityKey.Set(entity, _construct(values));

    public object?[] Split(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _components.Select(component => component.Get(key)).ToArray();
    }

    private static Func<object?[], object> CompileConstructor(Type keyType, MappingMemberAccess[] components)
    {
        var constructors = keyType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        foreach (var constructor in constructors.OrderByDescending(static candidate => candidate.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length != components.Length) continue;
            var indexes = new int[parameters.Length];
            var valid = true;
            for (var index = 0; index < parameters.Length; index++)
            {
                var match = Array.FindIndex(components, component =>
                    string.Equals(component.Path.Leaf, parameters[index].Name, StringComparison.OrdinalIgnoreCase) &&
                    parameters[index].ParameterType == component.ValueType);
                if (match < 0 || indexes.Take(index).Contains(match)) { valid = false; break; }
                indexes[index] = match;
            }
            if (!valid) continue;

            var values = Expression.Parameter(typeof(object[]), "values");
            var arguments = parameters.Select((parameter, index) =>
                Expression.Convert(
                    Expression.ArrayIndex(values, Expression.Constant(indexes[index])),
                    parameter.ParameterType));
            return Expression.Lambda<Func<object?[], object>>(
                Expression.Convert(Expression.New(constructor, arguments), typeof(object)),
                values).Compile();
        }

        throw new InvalidOperationException(
            $"Composite identity '{keyType.FullName}' requires one public constructor whose parameter names and types match every declared part.");
    }
}
