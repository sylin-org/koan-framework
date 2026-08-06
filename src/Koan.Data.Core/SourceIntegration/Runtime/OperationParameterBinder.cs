using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Core.Options;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.SourceIntegration.Runtime;

internal sealed class OperationParameterBinder
{
    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, AccessorPlan> _plans = new();
    private readonly Queue<CacheKey> _order = new();
    private readonly int _capacity;

    public OperationParameterBinder(IOptions<SourceIntegrationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _capacity = options.Value.ParameterPlanCacheEntries;
        if (_capacity <= 0)
            throw new InvalidOperationException("SourceIntegration ParameterPlanCacheEntries must be positive.");
    }

    public IReadOnlyList<BoundOperationParameter> Bind(OperationPlan operation, object? values)
    {
        if (values is null)
        {
            if (operation.Parameters.Count == 0) return [];
            throw Invalid(operation, $"Missing required parameters: {Names(operation.Parameters)}.");
        }

        if (values is IEnumerable<KeyValuePair<string, object?>> generic)
            return BindDictionary(operation, generic);
        if (values is IDictionary dictionary)
            return BindDictionary(operation, ReadDictionary(operation, dictionary));

        var key = new CacheKey(operation.Source, operation.Name, values.GetType());
        AccessorPlan plan;
        lock (_gate)
        {
            if (!_plans.TryGetValue(key, out plan!))
            {
                plan = Compile(operation, values.GetType());
                if (_plans.Count >= _capacity)
                {
                    var oldest = _order.Dequeue();
                    _plans.Remove(oldest);
                }
                _plans.Add(key, plan);
                _order.Enqueue(key);
            }
        }

        var result = new BoundOperationParameter[plan.Accessors.Length];
        for (var index = 0; index < plan.Accessors.Length; index++)
        {
            var accessor = plan.Accessors[index];
            object? value;
            try
            {
                value = accessor.Get(values);
            }
            catch (Exception error)
            {
                throw Invalid(operation, $"Parameter '{accessor.Parameter.Name}' could not be read from the parameter object.", error);
            }
            ValidateValue(operation, accessor.Parameter, value);
            result[index] = new BoundOperationParameter(
                accessor.Parameter.Name,
                accessor.Parameter.ValueType,
                value);
        }
        return result;
    }

    private static IReadOnlyList<BoundOperationParameter> BindDictionary(
        OperationPlan operation,
        IEnumerable<KeyValuePair<string, object?>> values)
    {
        var supplied = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(name) || !supplied.TryAdd(name, value))
                throw Invalid(operation, "Parameter names must be non-blank and unique ignoring case.");
        }

        var declared = operation.Parameters.ToDictionary(
            static parameter => parameter.Name,
            StringComparer.OrdinalIgnoreCase);
        var extra = supplied.Keys.Where(name => !declared.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
        var missing = declared.Keys.Where(name => !supplied.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
        if (extra.Length > 0) throw Invalid(operation, $"Unexpected parameters: {string.Join(", ", extra)}.");
        if (missing.Length > 0) throw Invalid(operation, $"Missing required parameters: {string.Join(", ", missing)}.");

        var result = new BoundOperationParameter[operation.Parameters.Count];
        for (var index = 0; index < operation.Parameters.Count; index++)
        {
            var parameter = operation.Parameters[index];
            var value = supplied[parameter.Name];
            ValidateValue(operation, parameter, value);
            result[index] = new BoundOperationParameter(parameter.Name, parameter.ValueType, value);
        }
        return result;
    }

    private static IEnumerable<KeyValuePair<string, object?>> ReadDictionary(
        OperationPlan operation,
        IDictionary values)
    {
        foreach (DictionaryEntry entry in values)
        {
            if (entry.Key is not string name)
                throw Invalid(operation, "A parameter dictionary may contain only string keys.");
            yield return new KeyValuePair<string, object?>(name, entry.Value);
        }
    }

    private static AccessorPlan Compile(OperationPlan operation, Type runtimeType)
    {
        var members = runtimeType
            .GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .Where(static member => member is PropertyInfo { CanRead: true, GetMethod.IsStatic: false } property &&
                                      property.GetIndexParameters().Length == 0 ||
                                  member is FieldInfo { IsStatic: false })
            .ToArray();
        var byName = new Dictionary<string, MemberInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
            if (!byName.TryAdd(member.Name, member))
                throw Invalid(operation, "The parameter object has member names that collide ignoring case.");

        var declared = operation.Parameters.Select(static parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extra = byName.Keys.Where(name => !declared.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        var missing = declared.Where(name => !byName.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
        if (extra.Length > 0) throw Invalid(operation, $"Unexpected parameters: {string.Join(", ", extra)}.");
        if (missing.Length > 0) throw Invalid(operation, $"Missing required parameters: {string.Join(", ", missing)}.");

        var accessors = new MemberAccessor[operation.Parameters.Count];
        for (var index = 0; index < operation.Parameters.Count; index++)
        {
            var parameter = operation.Parameters[index];
            var member = byName[parameter.Name];
            var instance = Expression.Parameter(typeof(object), "parameters");
            var converted = Expression.Convert(instance, runtimeType);
            Expression read = member switch
            {
                PropertyInfo property => Expression.Property(converted, property),
                FieldInfo field => Expression.Field(converted, field),
                _ => throw new UnreachableException()
            };
            var getter = Expression.Lambda<Func<object, object?>>(
                Expression.Convert(read, typeof(object)),
                instance).Compile();
            accessors[index] = new MemberAccessor(parameter, getter);
        }
        return new AccessorPlan(accessors);
    }

    private static void ValidateValue(OperationPlan operation, OperationParameter parameter, object? value)
    {
        if (value is null)
        {
            if (parameter.Required)
                throw Invalid(operation, $"Required parameter '{parameter.Name}' cannot be null.");
            return;
        }

        var expected = Nullable.GetUnderlyingType(parameter.ValueType) ?? parameter.ValueType;
        if (!expected.IsInstanceOfType(value))
            throw Invalid(
                operation,
                $"Parameter '{parameter.Name}' requires '{parameter.ValueType.FullName}', not '{value.GetType().FullName}'.");
    }

    private static string Names(IEnumerable<OperationParameter> parameters) =>
        string.Join(", ", parameters.Select(static parameter => parameter.Name));

    private static OperationParameterException Invalid(
        OperationPlan operation,
        string correction,
        Exception? inner = null)
    {
        var exception = new OperationParameterException(operation.Source, operation.Name, correction);
        return inner is null ? exception : new OperationParameterException(
            operation.Source,
            operation.Name,
            $"{correction} ({inner.GetType().Name})");
    }

    private readonly record struct CacheKey(string Source, string Operation, Type RuntimeType);
    private sealed record AccessorPlan(MemberAccessor[] Accessors);
    private sealed record MemberAccessor(OperationParameter Parameter, Func<object, object?> Get);
}
