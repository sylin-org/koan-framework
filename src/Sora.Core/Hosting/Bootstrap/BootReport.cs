using System.Text;

namespace Sora.Core.Hosting.Bootstrap;

// Greenfield bootstrap report collected from module registrars.
public sealed class BootReport
{
    private readonly List<(string Name, string? Version, List<(string Key, string Value, bool Secret)> Settings, List<string> Notes)> _modules = new();
    private readonly List<DecisionLogEntry> _decisions = new();

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
        var sb = new StringBuilder();
        
        if (options.ShowDecisions && _decisions.Any())
        {
            // Show decisions first for better narrative flow
            foreach (var decision in _decisions)
            {
                FormatDecision(sb, decision, options);
            }
            if (_modules.Any()) sb.AppendLine(); // Separator between decisions and modules
        }

        // Show module summary
        foreach (var m in _modules)
        {
            if (options.CompactMode)
            {
                // Compact: [Sora.Data.Mongo] loaded
                sb.Append("[").Append(m.Name).Append("] loaded");
                if (!string.IsNullOrWhiteSpace(m.Version)) sb.Append(" (").Append(m.Version).Append(")");
                sb.AppendLine();
            }
            else
            {
                // Detailed: [Sora] module Sora.Data.Mongo 2.1.0: setting=value;
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
        }
        return sb.ToString();
    }

    private static void FormatDecision(StringBuilder sb, DecisionLogEntry decision, BootReportOptions options)
    {
        switch (decision.Type)
        {
            case DecisionType.ConnectionAttempt:
                if (!options.ShowConnectionAttempts) return;
                sb.Append("[Sora.").Append(decision.Category).Append("] connection attempt: ");
                if (!string.IsNullOrEmpty(decision.ConnectionString))
                {
                    sb.Append(Sora.Core.Redaction.DeIdentify(decision.ConnectionString));
                }
                sb.Append(" (").Append(decision.Decision);
                if (decision.Decision == "failed" && !string.IsNullOrEmpty(decision.Reason))
                {
                    sb.Append(": ").Append(decision.Reason);
                }
                sb.AppendLine(")");
                break;

            case DecisionType.ProviderElection:
                sb.Append("[Sora.").Append(decision.Category).Append("] provider elected: ");
                sb.Append(decision.Decision);
                sb.Append(" (").Append(decision.Reason);
                if (decision.Alternatives.Any())
                {
                    sb.Append(", available: ").Append(string.Join(", ", decision.Alternatives));
                }
                sb.AppendLine(")");
                break;

            case DecisionType.Discovery:
                if (!options.ShowDiscovery) return;
                sb.Append("[Sora.").Append(decision.Category).Append("] ").Append(decision.Decision);
                sb.Append(" (").Append(decision.Reason).AppendLine(")");
                break;

            case DecisionType.Decision:
                sb.Append("[Sora.").Append(decision.Category).Append("] ").Append(decision.Decision);
                sb.Append(" (").Append(decision.Reason);
                if (decision.Alternatives.Any())
                {
                    sb.Append(", alternatives: ").Append(string.Join(", ", decision.Alternatives));
                }
                sb.AppendLine(")");
                break;
        }
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

public class BootReportOptions
{
    public bool ShowDecisions { get; set; } = true;
    public bool ShowConnectionAttempts { get; set; } = true;
    public bool ShowDiscovery { get; set; } = true;
    public bool CompactMode { get; set; } = false;
}
