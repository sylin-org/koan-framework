using System.Text.Json.Nodes;

namespace Koan.Mcp.Schema;

internal static class SchemaExtensions
{
    public static JsonObject CreateObjectSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject()
        };
    }

    public static JsonObject CreateStringProperty(string description)
    {
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description
        };
    }

    public static JsonObject CreateBooleanProperty(string description)
    {
        return new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = description
        };
    }

    public static JsonObject WithDescription(this JsonObject schema, string? description)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            schema["description"] = description;
        }

        return schema;
    }
}
