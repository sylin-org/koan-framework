using System.Text.Json;

namespace Koan.Jobs.Support;

internal static class JobSerialization
{
    internal static string? Serialize<T>(T value)
    {
        if (value is null)
            return null;
        return JsonSerializer.Serialize(value, JobEnvironment.Options.SerializerOptions);
    }

    internal static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        return JsonSerializer.Deserialize<T>(json, JobEnvironment.Options.SerializerOptions);
    }
}
