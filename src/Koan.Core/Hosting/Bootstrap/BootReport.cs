using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Koan.Core.Hosting.Bootstrap;

// Greenfield bootstrap report collected from module registrars.
public sealed class BootReport
{
    private readonly List<BootModuleBuilder> _modules = new();
    private readonly List<DecisionLogEntry> _decisions = new();

    public void AddModule(string name, string? version = null, string? description = null)
        => _modules.Add(new BootModuleBuilder(name, version) { Description = description });

    public int ModuleCount => _modules.Count;

    public void SetModuleDescription(int index, string? description)
    {
        if (index < 0 || index >= _modules.Count) return;
        _modules[index].Description = description;
    }

    public void AddSetting(
        string key,
        string? value,
        bool isSecret = false,
        BootSettingSource source = BootSettingSource.Unknown,
        IReadOnlyCollection<string>? consumers = null,
        string? sourceKey = null)
    {
        if (_modules.Count == 0) return;

        var sanitized = value ?? "(null)";
        if (isSecret)
        {
            sanitized = Koan.Core.Redaction.DeIdentify(sanitized);
        }

        _modules[^1].Settings.Add(new BootModuleSettingEntry(
            key,
            sanitized,
            isSecret,
            source,
            sourceKey ?? string.Empty,
            consumers is null ? Array.Empty<string>() : consumers.ToArray()));
    }

    public void AddNote(string message)
    {
        if (_modules.Count == 0) return;
        _modules[^1].Notes.Add(message);
    }

    public void AddTool(string name, string route, string? description = null, string? capability = null)
    {
        if (_modules.Count == 0) return;

        _modules[^1].Tools.Add(new BootModuleToolEntry(name, route, description, capability));
    }

    public IReadOnlyList<BootModule> GetModules()
        => _modules
            .Select(m => new BootModule(
                m.Name,
                m.Version,
                m.Description,
                m.Settings.Count == 0 && m.Notes.Count == 0 && m.Tools.Count == 0,
                m.Settings.Select(s => new BootModuleSetting(s.Key, s.Value, s.Secret, s.Source, s.SourceKey, s.Consumers)).ToList(),
                m.Notes.AsReadOnly(),
                m.Tools.Select(t => new BootModuleTool(t.Name, t.Route, t.Description, t.Capability)).ToList()))
            .ToList();

    // NEW: Decision logging methods
    public void AddDecision(string category, string decision, string reason, string[]? alternatives = null)
    {
        _decisions.Add(new DecisionLogEntry
        {
            Type = DecisionType.Decision,
            Category = category,
            Decision = decision,
            Reason = reason,
            Alternatives = alternatives ?? Array.Empty<string>()
        });
    }

    public void AddConnectionAttempt(string provider, string connectionString, bool success, string? error = null)
    {
        _decisions.Add(new DecisionLogEntry
        {
            Type = DecisionType.ConnectionAttempt,
            Category = provider,
            Decision = success ? "success" : "failed",
            Reason = error ?? (success ? "connection successful" : "connection failed"),
            ConnectionString = connectionString
        });
    }

    public void AddProviderElection(string category, string selected, string[] available, string reason)
    {
        _decisions.Add(new DecisionLogEntry
        {
            Type = DecisionType.ProviderElection,
            Category = category,
            Decision = selected,
            Reason = reason,
            Alternatives = available
        });
    }

    public void AddDiscovery(string source, string value, bool success = true)
    {
        _decisions.Add(new DecisionLogEntry
        {
            Type = DecisionType.Discovery,
            Category = source,
            Decision = value,
            Reason = success ? "discovered" : "not found"
        });
    }

    public override string ToString()
    {
        return ToString(new BootReportOptions());
    }

    public string ToString(BootReportOptions options)
    {
        return FormatWithKoanStyle(options);
    }

    private string FormatWithKoanStyle(BootReportOptions options)
    {
        var sb = new StringBuilder();
        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        // Framework header with version info
    var coreModule = _modules.FirstOrDefault(m => m.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
    var frameworkVersion = coreModule is not null ? (coreModule.Version ?? "unknown") : "unknown";

        var headerText = $"Koan FRAMEWORK v{frameworkVersion}";
        var lineLength = 80;
        var padding = new string('─', Math.Max(0, lineLength - headerText.Length - 4));
        sb.AppendLine($"┌─ {headerText} {padding}");
        sb.AppendLine($"│ Core: {frameworkVersion}");

        // Module hierarchy
        foreach (var module in _modules.Where(m => !m.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"│   ├─ {module.Name}: {module.Version ?? "unknown"}");
        }

        if (_modules.Any(m => !m.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
        {
            var lastModule = _modules.Where(m => !m.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).LastOrDefault();
            if (lastModule != default)
            {
                sb.AppendLine($"│   └─ {lastModule.Name}: {lastModule.Version ?? "unknown"}");
            }
        }

        // Startup phase
        if (options.ShowDecisions && _decisions.Any())
        {
            var startupText = "STARTUP";
            var startupPadding = new string('─', Math.Max(0, lineLength - startupText.Length - 4));
            sb.AppendLine($"├─ {startupText} {startupPadding}");

            foreach (var decision in _decisions)
            {
                FormatDecisionKoanStyle(sb, decision, timestamp, options);
            }
        }

        return sb.ToString();
    }

    private static void FormatDecisionKoanStyle(StringBuilder sb, DecisionLogEntry decision, string timestamp, BootReportOptions options)
    {
        switch (decision.Type)
        {
            case DecisionType.ConnectionAttempt:
                if (!options.ShowConnectionAttempts) return;
                var status = decision.Decision == "success" ? "OK" : "FAIL";
                FormatLogLine(sb, "I", timestamp, "Koan:discover", $"{decision.Category}: {Koan.Core.Redaction.DeIdentify(decision.ConnectionString ?? "")} {status}");
                break;

            case DecisionType.ProviderElection:
                FormatLogLine(sb, "I", timestamp, "Koan:modules", $"{decision.Category.ToLower()}→{decision.Decision}");
                break;

            case DecisionType.Discovery:
                if (!options.ShowDiscovery) return;
                FormatLogLine(sb, "I", timestamp, "Koan:discover", $"{decision.Category}: {decision.Decision}");
                break;
        }
    }

    private static void FormatLogLine(StringBuilder sb, string level, string timestamp, string context, string message)
    {
        var paddedContext = context.PadRight(15); // Consistent column width handled here
        sb.AppendLine($"│ {level} {timestamp} {paddedContext} {message}");
    }

}

// Supporting types for decision logging
public enum DecisionType
{
    Decision,
    ConnectionAttempt,
    ProviderElection,
    Discovery
}

public class DecisionLogEntry
{
    public DecisionType Type { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string[] Alternatives { get; set; } = Array.Empty<string>();
    public string? ConnectionString { get; set; }
}

public enum BootSettingSource
{
    Unknown,
    Auto,
    AppSettings,
    Environment,
    LaunchKit,
    Custom
}

public sealed record BootModule(
    string Name,
    string? Version,
    string? Description,
    bool IsStub,
    IReadOnlyList<BootModuleSetting> Settings,
    IReadOnlyList<string> Notes,
    IReadOnlyList<BootModuleTool> Tools);

public sealed record BootModuleSetting(
    string Key,
    string Value,
    bool Secret,
    BootSettingSource Source,
    string SourceKey,
    IReadOnlyList<string> Consumers);

public sealed record BootModuleTool(
    string Name,
    string Route,
    string? Description,
    string? Capability);

public class BootReportOptions
{
    public bool ShowDecisions { get; set; } = true;
    public bool ShowConnectionAttempts { get; set; } = true;
    public bool ShowDiscovery { get; set; } = true;
    public bool CompactMode { get; set; } = false;
}

internal sealed class BootModuleBuilder
{
    public BootModuleBuilder(string name, string? version)
    {
        Name = name;
        Version = version;
    }

    public string Name { get; }
    public string? Version { get; }
    public string? Description { get; set; }
    public List<BootModuleSettingEntry> Settings { get; } = new();
    public List<string> Notes { get; } = new();
    public List<BootModuleToolEntry> Tools { get; } = new();
}

internal sealed record BootModuleSettingEntry(
    string Key,
    string Value,
    bool Secret,
    BootSettingSource Source,
    string SourceKey,
    IReadOnlyList<string> Consumers);

internal sealed record BootModuleToolEntry(
    string Name,
    string Route,
    string? Description,
    string? Capability);
