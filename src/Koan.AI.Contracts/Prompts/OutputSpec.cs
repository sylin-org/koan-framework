using System.Reflection;
using System.Text.Json;

namespace Koan.AI.Prompt;

/// <summary>
/// Describes expected prompt output structure. Used to generate JSON schema constraints
/// sent to the model and to validate/parse responses.
/// </summary>
public sealed record OutputSpec
{
    /// <summary>Output format name (e.g., "json", "csv", "enum").</summary>
    public string Format { get; init; } = "json";

    /// <summary>JSON schema string (for structured output).</summary>
    public string? JsonSchema { get; init; }

    /// <summary>Field names expected in the output.</summary>
    public IReadOnlyList<string> ExpectedFields { get; init; } = [];

    /// <summary>The CLR type this output maps to (for deserialization).</summary>
    public Type? TargetType { get; init; }

    /// <summary>Create an OutputSpec from a type's public properties.</summary>
    public static OutputSpec FromType<T>()
    {
        var type = typeof(T);
        var fields = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p => p.Name)
            .ToList();

        return new OutputSpec
        {
            Format = "json",
            ExpectedFields = fields,
            TargetType = type,
            JsonSchema = GenerateSchema(type)
        };
    }

    /// <summary>Create an OutputSpec from explicit field names.</summary>
    public static OutputSpec WithFields(params string[] fields)
    {
        return new OutputSpec
        {
            Format = "json",
            ExpectedFields = fields.ToList()
        };
    }

    /// <summary>Generate instruction text for the model.</summary>
    public string ToInstructionText()
    {
        if (JsonSchema is not null)
            return $"Output valid JSON matching this schema:\n{JsonSchema}";

        if (ExpectedFields.Count > 0)
            return $"Output valid JSON with these fields: {string.Join(", ", ExpectedFields)}.";

        return $"Output in {Format} format.";
    }

    private static string GenerateSchema(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p => new
            {
                name = JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                type = MapClrTypeToJsonType(p.PropertyType),
                nullable = IsNullable(p.PropertyType)
            });

        var schemaObj = new
        {
            type = "object",
            properties = properties.ToDictionary(
                p => p.name,
                p => new { type = p.type, nullable = p.nullable ? true : (bool?)null }),
            required = properties.Where(p => !p.nullable).Select(p => p.name).ToList()
        };

        return JsonSerializer.Serialize(schemaObj, JsonOptions.Compact);
    }

    private static string MapClrTypeToJsonType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return "string";
        if (underlying == typeof(bool)) return "boolean";
        if (underlying == typeof(int) || underlying == typeof(long) ||
            underlying == typeof(short) || underlying == typeof(byte)) return "integer";
        if (underlying == typeof(float) || underlying == typeof(double) ||
            underlying == typeof(decimal)) return "number";
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) ||
            underlying == typeof(DateOnly)) return "string";
        if (underlying.IsEnum) return "string";
        if (underlying.IsArray || (underlying.IsGenericType &&
            underlying.GetGenericTypeDefinition() == typeof(List<>))) return "array";

        return "object";
    }

    private static bool IsNullable(Type type)
    {
        if (!type.IsValueType) return true;
        return Nullable.GetUnderlyingType(type) is not null;
    }
}
