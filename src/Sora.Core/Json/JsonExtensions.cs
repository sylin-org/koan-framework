using Newtonsoft.Json;

namespace Sora.Core.Json;

public static class JsonExtensions
{
    public static string ToJson(this object? value) => JsonConvert.SerializeObject(value, JsonDefaults.Settings);

    public static T? FromJson<T>(this string json) => JsonConvert.DeserializeObject<T>(json, JsonDefaults.Settings);
}
