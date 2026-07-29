using System.Linq.Expressions;
using System.Reflection;

namespace Koan.Data.Abstractions;

internal static class RecordProjector
{
    private static readonly MethodInfo ReadMethod = typeof(RecordProjector)
        .GetMethod(
            nameof(Read),
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(DataRecord), typeof(int), typeof(Type)],
            modifiers: null)!;

    public static Func<DataRecord, T> Compile<T>(IReadOnlyList<DataField> fields)
    {
        var target = typeof(T);
        var record = Expression.Parameter(typeof(DataRecord), "record");
        var constructors = target.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(static constructor => constructor.GetParameters().Length > 0)
            .OrderByDescending(static constructor => constructor.GetParameters().Length)
            .ToArray();

        var viable = constructors
            .Select(constructor => TryConstructor(constructor, fields, record))
            .Where(static candidate => candidate is not null)
            .ToArray();
        if (viable.Length > 0)
        {
            var maximum = viable.Max(static candidate => candidate!.ParameterCount);
            var best = viable.Where(candidate => candidate!.ParameterCount == maximum).ToArray();
            if (best.Length != 1)
                throw new RecordProjectionException(target, "Multiple public constructors match the record shape equally.");
            return Expression.Lambda<Func<DataRecord, T>>(best[0]!.Expression, record).Compile();
        }

        var empty = target.GetConstructor(Type.EmptyTypes);
        if (empty is null)
            throw new RecordProjectionException(target, "Provide one unambiguous matching public constructor or a public parameterless constructor with writable properties.");

        var bindings = new List<MemberBinding>();
        foreach (var property in target.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not { IsPublic: true }) continue;
            var ordinal = UniqueOrdinal(fields, property.Name, target);
            if (ordinal is null) continue;
            bindings.Add(Expression.Bind(property, Read(record, fields[ordinal.Value], property.PropertyType)));
        }
        return Expression.Lambda<Func<DataRecord, T>>(
            Expression.MemberInit(Expression.New(empty), bindings), record).Compile();
    }

    private static ConstructorCandidate? TryConstructor(
        ConstructorInfo constructor,
        IReadOnlyList<DataField> fields,
        ParameterExpression record)
    {
        var parameters = constructor.GetParameters();
        var arguments = new Expression[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.Name is null) return null;
            var ordinal = UniqueOrdinal(fields, parameter.Name, constructor.DeclaringType!);
            if (ordinal is null)
            {
                if (parameter.HasDefaultValue)
                {
                    arguments[i] = Expression.Constant(parameter.DefaultValue, parameter.ParameterType);
                    continue;
                }
                return null;
            }
            arguments[i] = Read(record, fields[ordinal.Value], parameter.ParameterType);
        }
        return new ConstructorCandidate(parameters.Length, Expression.New(constructor, arguments));
    }

    private static Expression Read(ParameterExpression record, DataField field, Type targetType)
        => Expression.Convert(
            Expression.Call(
                ReadMethod,
                record,
                Expression.Constant(field.Ordinal),
                Expression.Constant(targetType, typeof(Type))),
            targetType);

    private static object? Read(DataRecord record, int ordinal, Type targetType)
    {
        var field = record.Field(ordinal);
        if (!record.TryGetValue(ordinal, out var value)) throw new RecordValueMissingException(field);
        return NeutralDataValue.ConvertTo(value, targetType, field);
    }

    private static int? UniqueOrdinal(IReadOnlyList<DataField> fields, string name, Type target)
    {
        int? found = null;
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (found is not null)
                throw new RecordProjectionException(
                    target,
                    $"Field name '{name}' is duplicated; DTO binding requires one unique field.");
            found = i;
        }
        return found;
    }

    private sealed record ConstructorCandidate(int ParameterCount, NewExpression Expression);
}
