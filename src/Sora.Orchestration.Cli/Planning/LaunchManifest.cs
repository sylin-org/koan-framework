using Sora.Orchestration;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sora.Orchestration.Infrastructure;

namespace Sora.Orchestration.Cli.Planning;

internal static class LaunchManifest
{
    internal sealed class Model
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("app")] public AppInfo App { get; set; } = new();
        [JsonPropertyName("options")] public Options Opt { get; set; } = new();
        [JsonPropertyName("allocations")] public Dictionary<string, Allocation> Allocations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public sealed class AppInfo
        {
            [JsonPropertyName("id")] public string? Id { get; set; }
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("code")] public string? Code { get; set; }
            [JsonPropertyName("defaultPublicPort")] public int? DefaultPublicPort { get; set; }
            [JsonPropertyName("assignedPublicPort")] public int? AssignedPublicPort { get; set; }
        }
        public sealed class Options
        {
            [JsonPropertyName("exposeInternals")] public bool ExposeInternals { get; set; }
            [JsonPropertyName("provider")] public string? Provider { get; set; }
            [JsonPropertyName("lastProfile")] public string? LastProfile { get; set; }
        }
        public sealed class Allocation { [JsonPropertyName("assignedPublicPort")] public int? AssignedPublicPort { get; set; } }
    }

    public static Model? Load(string cwd)
    {
        try
        {
            var path = Path.Combine(cwd, OrchestrationConstants.LaunchManifestPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Model>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    public static void Save(string cwd, Model model)
    {
        try
        {
            var path = Path.Combine(cwd, OrchestrationConstants.LaunchManifestPath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path);
                    if (!string.Equals(existing, json, StringComparison.Ordinal))
                    {
                        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                        var backup = Path.Combine(dir!, $"manifest.json.old.{stamp}");
                        File.Move(path, backup, overwrite: false);
                    }
                }
            }
            catch { }
            File.WriteAllText(path, json);
            // Ensure .sora/.gitignore guards local files
            var gi = Path.Combine(dir!, ".gitignore");
            try { if (!File.Exists(gi)) File.WriteAllText(gi, "*\n!compose.yml\n"); } catch { }
        }
        catch { }
    }
}
