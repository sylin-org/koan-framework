using System.Text;

namespace Sora.Core.Hosting.Bootstrap;

// Greenfield bootstrap report collected from module registrars.
public sealed class BootReport
{
    private readonly List<(string Name, string? Version, List<(string Key, string Value, bool Secret)> Settings, List<string> Notes)> _modules = new();

    public void AddModule(string name, string? version = null)
        => _modules.Add((name, version, new(), new()));

    public void AddSetting(string key, string? value, bool isSecret = false)
    {
        if (_modules.Count == 0) return;
        var v = value ?? "(null)";
        if (isSecret) v = Sora.Core.Redaction.DeIdentify(v);
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
