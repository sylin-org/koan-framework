using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Infrastructure;

/// <summary>
/// Normalizes extracted values so they comply with the target JSON schema.
/// </summary>
public static class SchemaValueCoercion
{
    public static bool TryCoerce(string? rawValue, JSchema schema, out JToken? normalizedToken, out string? validationError)
    {
        var token = rawValue is null
            ? JValue.CreateNull()
            : new JValue(rawValue.Trim());
        return TryCoerce(token, schema, out normalizedToken, out validationError);
    }

    public static bool TryCoerce(JToken? valueToken, JSchema schema, out JToken? normalizedToken, out string? validationError)
    {
        normalizedToken = valueToken;
        validationError = null;

        if (schema is null)
        {
            throw new ArgumentNullException(nameof(schema));
        }

        if (valueToken is null || valueToken.Type == JTokenType.Null)
        {
            return true;
        }

        var working = valueToken.DeepClone();

        if (schema.Type.HasValue)
        {
            var types = schema.Type.Value;

            if ((types.HasFlag(JSchemaType.Number) || types.HasFlag(JSchemaType.Integer)) && working is JValue numericValue)
            {
                if (numericValue.Type == JTokenType.String)
                {
                    var text = numericValue.Value<string>()?.Trim();
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber))
                    {
                        working = types.HasFlag(JSchemaType.Integer)
                            ? new JValue(Convert.ToInt64(Math.Round(parsedNumber)))
                            : new JValue(parsedNumber);
                    }
                }
            }
            else if (types.HasFlag(JSchemaType.Boolean) && working is JValue boolValue && boolValue.Type == JTokenType.String)
            {
                var text = boolValue.Value<string>()?.Trim();
                if (bool.TryParse(text, out var parsedBool))
                {
                    working = new JValue(parsedBool);
                }
            }
            else if (types.HasFlag(JSchemaType.String) && working.Type != JTokenType.String)
            {
                working = new JValue(working.ToString(Formatting.None));
            }
        }

        if (!working.IsValid(schema, out IList<string> errors))
        {
            validationError = string.Join("; ", errors);
            normalizedToken = valueToken;
            return false;
        }

        normalizedToken = working;
        return true;
    }
}
