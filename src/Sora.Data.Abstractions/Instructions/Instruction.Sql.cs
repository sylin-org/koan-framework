using System;
using System.Collections.Generic;
using System.Linq;

namespace Sora.Data.Abstractions.Instructions;

public static class InstructionSql
{
    public static Instruction NonQuery(string sql, object? parameters = null)
        => new("relational.sql.nonquery", new { Sql = sql }, ToDictionary(parameters));

    public static Instruction Scalar(string sql, object? parameters = null)
        => new("relational.sql.scalar", new { Sql = sql }, ToDictionary(parameters));

    public static Instruction Query(string sql, object? parameters = null)
        => new("relational.sql.query", new { Sql = sql }, ToDictionary(parameters));

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
