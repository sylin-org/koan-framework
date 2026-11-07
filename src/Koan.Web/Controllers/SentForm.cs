using System.Collections;
using Koan.Data.Core.Commands;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Web.Controllers;

/// <summary>
/// A page's form as a declared entity operation reads it (WEB-0088): the JSON object a sender reading JSON would have sent, each field under
/// the name it was sent as and typed as the member it names, so a form and JSON are admitted, written and refused by one path. The fields a
/// form carries for the framework (<see cref="KoanWebConstants.Form"/>) are not among them.
/// </summary>
internal static class SentForm
{
    /// <summary>The members <paramref name="form"/> names as one object: a field naming a member of the entity or of the input of
    /// <paramref name="operation"/> takes that member's type, an empty one being no value where the member is not text, a collection takes
    /// every value sent, and any other field stays the text it was so the operation refuses it by name. A value that cannot be its member's
    /// type is the refusal, naming its field, as it is where it was sent as JSON.</summary>
    public static (JObject Words, Reason? Unfit) Words(IFormCollection form, EntityOperation operation, JsonSerializer serializer)
    {
        var words = new JObject();
        foreach (var (name, values) in form)
        {
            if (string.Equals(name, KoanWebConstants.Form.Method, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, KoanWebConstants.Form.Return, StringComparison.OrdinalIgnoreCase)) continue;
            var type = operation.Member(name)?.PropertyType;
            if (type is not null && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
            {
                words[name] = new JArray(values.Select(value => (JToken)new JValue(value)));
                continue;
            }

            var text = values.Count > 0 ? values[^1] : null;
            if (type is null || type == typeof(string) || type == typeof(object))
            {
                words[name] = new JValue(text);
                continue;
            }

            if (string.IsNullOrEmpty(text))
            {
                words[name] = JValue.CreateNull();
                continue;
            }

            try
            {
                words[name] = JToken.FromObject(new JValue(text).ToObject(type, serializer)!, serializer);
            }
            catch (Exception error) when (error is JsonException or FormatException or InvalidCastException or ArgumentException or OverflowException)
            {
                return (words, EntityOperation.CannotTake(name));
            }
        }
        return (words, null);
    }

    /// <summary>What <paramref name="form"/> sent, each field with its values, but the method it asked for.</summary>
    public static IReadOnlyDictionary<string, string[]> Fields(IFormCollection form)
        => form.Where(field => !string.Equals(field.Key, KoanWebConstants.Form.Method, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(field => field.Key, field => field.Value.Select(value => value ?? "").ToArray(), StringComparer.Ordinal);
}
