using Koan.Orchestration;
using Newtonsoft.Json;
using Koan.Orchestration.Infrastructure;

namespace Koan.Orchestration.Cli.Planning;

internal static class LaunchManifest
{
    internal sealed class Model
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;
        [JsonProperty("app")] public AppInfo App { get; set; } = new();
        [JsonProperty("options")] public Options Opt { get; set; } = new();
        [JsonProperty("allocations")] public Dictionary<string, Allocation> Allocations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public sealed class AppInfo
        {
            [JsonProperty("id")] public string? Id { get; set; }
            [JsonProperty("name")] public string? Name { get; set; }
            [JsonProperty("code")] public string? Code { get; set; }
            [JsonProperty("defaultPublicPort")] public int? DefaultPublicPort { get; set; }
            [JsonProperty("assignedPublicPort")] public int? AssignedPublicPort { get; set; }
        }
        public sealed class Options
        {
            [JsonProperty("exposeInternals")] public bool ExposeInternals { get; set; }
            [JsonProperty("provider")] public string? Provider { get; set; }
            [JsonProperty("lastProfile")] public string? LastProfile { get; set; }
        }
        public sealed class Allocation { [JsonProperty("assignedPublicPort")] public int? AssignedPublicPort { get; set; } }
    }

    public static Model? Load(string cwd)
    {
        try
        {
            var path = Path.Combine(cwd, OrchestrationConstants.LaunchManifestPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Model>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore, NullValueHandling = NullValueHandling.Include });
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
            var json = JsonConvert.SerializeObject(model, Newtonsoft.Json.Formatting.Indented);
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
            // Ensure .Koan/.gitignore guards local files
            var gi = Path.Combine(dir!, ".gitignore");
            try { if (!File.Exists(gi)) File.WriteAllText(gi, "*\n!compose.yml\n"); } catch { }
        }
        catch { }
    }
}
