using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Schema;

/// <summary>
/// AN11 (docs/assessment/09 §14 — A3, invariant #16) — "did you mean?". Projects a correction for a bad
/// payload from SCHEMA FACTS ONLY (enum members, required fields) by walking the tool's generated input
/// schema against the supplied arguments. It NEVER touches row data / counts / which records exist — the
/// schema-not-rows boundary is walled-means-silent (#6) re-applied to the error channel, so the existence
/// leak the AN-leak fix closed on the read path cannot reopen through validation errors.
/// </summary>
internal static class SchemaValidator
{
    /// <summary>Returns the schema-derived issues for the arguments, or an empty array when they conform.
    /// Each issue is <c>{ field, reason: "enum"|"required", provided?, validValues? }</c>.</summary>
    public static JArray Validate(JObject schema, JObject? instance)
    {
        var issues = new JArray();
        Walk(schema, instance ?? new JObject(), path: "", issues);
        return issues;
    }

    private static void Walk(JObject schema, JToken? instance, string path, JArray issues)
    {
        // Enum nodes are leaves: a provided scalar must be one of the declared members (case-insensitive,
        // mirroring Enum.Parse(ignoreCase) on the bind path so a valid value is never falsely flagged).
        if (schema["enum"] is JArray enumArr && instance is JValue value && value.Type != JTokenType.Null)
        {
            var provided = value.ToString();
            var member = enumArr.Any(e => string.Equals(e.Value<string>(), provided, StringComparison.OrdinalIgnoreCase));
            if (!member)
            {
                issues.Add(new JObject
                {
                    ["field"] = path,
                    ["reason"] = "enum",
                    ["provided"] = provided,
                    ["validValues"] = new JArray(enumArr.Select(e => (JToken)(e.Value<string>() ?? string.Empty)))
                });
            }
            return;
        }

        var type = schema["type"]?.Value<string>();
        if (string.Equals(type, "object", StringComparison.Ordinal) && schema["properties"] is JObject properties)
        {
            var obj = instance as JObject ?? new JObject();

            if (schema["required"] is JArray required)
            {
                foreach (var entry in required)
                {
                    var name = entry.Value<string>();
                    if (string.IsNullOrEmpty(name)) continue;
                    // Case-insensitive presence check (Newtonsoft binds member names case-insensitively).
                    if (obj.GetValue(name, StringComparison.OrdinalIgnoreCase) is null)
                    {
                        issues.Add(new JObject { ["field"] = Combine(path, name), ["reason"] = "required" });
                    }
                }
            }

            foreach (var property in properties.Properties())
            {
                var child = obj.GetValue(property.Name, StringComparison.OrdinalIgnoreCase);
                if (child is null) continue;
                if (property.Value is JObject childSchema)
                {
                    Walk(childSchema, child, Combine(path, property.Name), issues);
                }
            }
        }
        else if (string.Equals(type, "array", StringComparison.Ordinal) && schema["items"] is JObject itemsSchema && instance is JArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                Walk(itemsSchema, array[i], $"{path}[{i}]", issues);
            }
        }
    }

    private static string Combine(string path, string name) => string.IsNullOrEmpty(path) ? name : $"{path}.{name}";
}
