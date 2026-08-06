using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Abstractions.Instructions;

public static class InstructionSql
{
    public static Instruction NonQuery(string sql, object? parameters = null)
        => NonQuery(sql, DataOperationEffect.Unknown, parameters);

    public static Instruction NonQuery(
        string sql,
        DataOperationEffect effect,
        object? parameters = null)
        => Create(RelationalInstructions.SqlNonQuery, sql, effect, parameters);

    public static Instruction Scalar(string sql, object? parameters = null)
        => Scalar(sql, DataOperationEffect.Unknown, parameters);

    public static Instruction Scalar(
        string sql,
        DataOperationEffect effect,
        object? parameters = null)
        => Create(RelationalInstructions.SqlScalar, sql, effect, parameters);

    public static Instruction Query(string sql, object? parameters = null)
        => Query(sql, DataOperationEffect.Unknown, parameters);

    public static Instruction Query(
        string sql,
        DataOperationEffect effect,
        object? parameters = null)
        => Create(RelationalInstructions.SqlQuery, sql, effect, parameters);

    private static Instruction Create(
        string name,
        string sql,
        DataOperationEffect effect,
        object? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new Instruction(name, new { Sql = sql }, ToDictionary(parameters), Effect: effect);
    }

    internal static IReadOnlyDictionary<string, object?>? ToDictionary(object? parameters)
    {
        if (parameters is null) return null;
        if (parameters is IReadOnlyDictionary<string, object?> ro) return ro;
        if (parameters is IDictionary<string, object?> dict) return new Dictionary<string, object?>(dict);
        // anonymous object -> dictionary
        var props = parameters.GetType().GetProperties();
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            bag[p.Name] = p.GetValue(parameters);
        }
        return bag;
    }
}
