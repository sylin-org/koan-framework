using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Text;

namespace Sora.Core;

// Standard auto-registrar contract for Sora modules
public interface ISoraAutoRegistrar : ISoraInitializer
{
    string ModuleName { get; }
    string? ModuleVersion { get; }
    void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env);
}

public sealed class SoraBootstrapReport
{
    private readonly List<(string Name, string? Version, List<(string Key, string Value, bool Secret)> Settings, List<string> Notes)> _modules = new();

    public void AddModule(string name, string? version = null)
        => _modules.Add((name, version, new(), new()));

    public void AddSetting(string key, string? value, bool isSecret = false)
    {
        if (_modules.Count == 0) return;
        var v = value ?? "(null)";
        if (isSecret) v = Redaction.DeIdentify(v);
        _modules[^1].Settings.Add((key, v, isSecret));
    }

    public void AddNote(string message)
    {
        if (_modules.Count == 0) return;
        _modules[^1].Notes.Add(message);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var m in _modules)
        {
            sb.Append("[Sora] module ").Append(m.Name);
            if (!string.IsNullOrWhiteSpace(m.Version)) sb.Append(' ').Append(m.Version);
            if (m.Settings.Count == 0 && m.Notes.Count == 0) { sb.AppendLine(); continue; }
            sb.Append(':');
            foreach (var s in m.Settings)
            {
                sb.Append(' ').Append(s.Key).Append('=');
                sb.Append(s.Value);
                sb.Append(';');
            }
            foreach (var n in m.Notes)
            {
                sb.Append(' ').Append(n);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
