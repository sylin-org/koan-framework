using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Sora.Core.Json;

public static class JsonDefaults
{
    private static readonly JsonSerializerSettings _settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        Culture = CultureInfo.InvariantCulture,
        Formatting = Formatting.None
    };

    public static JsonSerializerSettings Settings => _settings;
}
