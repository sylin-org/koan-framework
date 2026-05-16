using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Observability.Health;

namespace Koan.Core.Logging;

internal static class KoanConsoleBlocks
{
    private const int DefaultWidth = 78;
    private const int LabelWidth = 14;

    public static string BuildStartupOverviewBlock(
        KoanEnvironmentSnapshot snapshot,
        string hostDescription,
        IReadOnlyList<(string Name, string Version)> modules,
        string runtimeVersion,
        RegistrySummarySnapshot? registry,
        HealthSnapshot? health)
    {
        var identity = snapshot.Application;
        var uniqueModules = DeduplicateModules(modules);

        var builder = new KoanConsoleBlockBuilder(KoanLogStage.Boot, "Application", DefaultWidth, "[KOAN]")
            .AddLine(FormatKeyValue("Name", identity.Name))
            .AddLine(FormatKeyValue("Code", identity.Code))
            .AddLine(FormatKeyValue("Environment", $"{snapshot.EnvironmentName} ({snapshot.OrchestrationMode})"))
            .AddLine(FormatKeyValue("Host", hostDescription))
            .AddLine(FormatKeyValue("Session", snapshot.SessionId))
            .AddLine(FormatKeyValue("Runtime", $"Koan.Core {runtimeVersion}"))
            .AddLine(FormatKeyValue("Timestamp", DateTimeOffset.UtcNow.ToString("o")));

        if (!string.IsNullOrWhiteSpace(identity.Description))
        {
            builder.AddLine(FormatKeyValue("Description", identity.Description));
        }

        if (!string.IsNullOrWhiteSpace(identity.ContactEmail))
        {
            builder.AddLine(FormatKeyValue("Contact", identity.ContactEmail));
        }

        if (!string.IsNullOrWhiteSpace(identity.SupportUrl))
        {
            builder.AddLine(FormatKeyValue("Support", identity.SupportUrl));
        }

        if (identity.Tags.Count > 0)
        {
            builder.AddLine(FormatKeyValue("Tags", string.Join(", ", identity.Tags)));
        }

        if (registry is not null)
        {
            builder.AddLine("");
            builder.AddLine(FormatSectionDivider("Registry"));
            builder.AddLine(FormatKeyValue("Initializers", FormatInitializerSummary(registry.Value)));
            builder.AddLine(FormatKeyValue("AutoReg", registry.Value.AutoRegistrars.ToString()));
            builder.AddLine(FormatKeyValue("Background", FormatBackgroundSummary(registry.Value)));
            builder.AddLine(FormatKeyValue("Adapters", registry.Value.ServiceDiscoveryAdapters.ToString()));
        }

        if (uniqueModules.Count > 0)
        {
            builder.AddLine("");
            builder.AddLine(FormatSectionDivider("Inventory"));

            foreach (var module in uniqueModules)
            {
                builder.AddLine(FormatKeyValue(module.Name, module.Version));
            }
        }

        builder.AddLine("");
        builder.AddLine(FormatSectionDivider("Environment"));
        builder
            .AddLine(FormatKeyValue("InContainer", snapshot.InContainer ? "true" : "false"))
            .AddLine(FormatKeyValue("Process", $"Started {snapshot.ProcessStart:o}"))
            .AddLine(FormatKeyValue("Uptime", FormatUptime(snapshot.ProcessStart)))
            .AddLine(FormatKeyValue("Machine", Environment.MachineName))
            .AddLine(FormatKeyValue("Health", FormatInventoryHealth(health)));

        return builder.Build();
    }

    public static string BuildReadyBlock(IEnumerable<string> urls, StartupTimelineSummary timeline, HealthSnapshot? health)
    {
        var addressList = urls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        var urlString = addressList.Count > 0 ? string.Join(", ", addressList) : "(not published)";
        var timing = FormatStartupPhases(timeline);
        var healthLine = FormatReadyHealth(health);

        var builder = new KoanConsoleBlockBuilder(KoanLogStage.Host, "Koan Ready", DefaultWidth)
                .AddLine(FormatKeyValue("Urls", urlString))
                .AddLine(FormatKeyValue("Timing", timing))
                .AddLine(FormatKeyValue("Health", healthLine));

        return builder.Build();
    }

    private static List<(string Name, string Version)> DeduplicateModules(IEnumerable<(string Name, string Version)> modules)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, version) in modules)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!result.ContainsKey(name))
            {
                result[name] = string.IsNullOrWhiteSpace(version) ? "unknown" : version;
            }
        }

        return result
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    private static string FormatKeyValue(string key, string value)
    {
        var label = key.Length > LabelWidth ? key : key.PadRight(LabelWidth);
        return $"{label}: {value}";
    }

    private static string FormatSectionDivider(string title)
    {
        var innerWidth = DefaultWidth - 3;
        var prefix = $"─ {title} ";
        var remaining = Math.Max(0, innerWidth - prefix.Length);
        return prefix + new string('─', remaining);
    }

    private static string FormatUptime(DateTimeOffset start)
    {
        var uptime = DateTimeOffset.UtcNow - start;
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }
        return uptime.ToString();
    }

    internal static string FormatStartupPhases(StartupTimelineSummary summary)
    {
        var segments = new (string Label, TimeSpan? Duration)[]
        {
            ("warmup", summary.Boot),
            ("registry", summary.Config),
            ("data", summary.Data),
            ("services", summary.Services),
            ("ready", summary.Total)
        };

        if (!segments.Any(s => s.Duration.HasValue))
        {
            return "(timing unavailable)";
        }

        return string.Join(" → ", segments.Select(s => $"{s.Label}({FormatDuration(s.Duration)})"));
    }

    private static string FormatInitializerSummary(RegistrySummarySnapshot summary)
    {
        var builder = new StringBuilder();
        builder.Append(summary.Initializers);

        if (summary.Initializers > 0 && summary.InitializerBreakdown.Count > 0)
        {
            var ordered = summary.InitializerBreakdown
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            var breakdown = ordered
                .Select(item => $"{TruncateNamespace(item.Namespace)}={item.Count}")
                .ToArray();

            if (breakdown.Length > 0)
            {
                builder.Append(" (");
                builder.Append(string.Join(", ", breakdown));
                if (summary.InitializerBreakdown.Count > breakdown.Length)
                {
                    builder.Append($", +{summary.InitializerBreakdown.Count - breakdown.Length}");
                }
                builder.Append(')');
            }
        }

        return builder.ToString();
    }

    private static string TruncateNamespace(string value)
    {
        const int maxLength = 24;
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "…";
    }

    private static string FormatBackgroundSummary(RegistrySummarySnapshot summary)
    {
        if (summary.BackgroundServices == 0)
        {
            return "0";
        }

        var segments = new List<string>
        {
            summary.BackgroundServices.ToString()
        };

        var details = new List<string>();
        if (summary.StartupBackgroundServices > 0)
        {
            details.Add($"startup={summary.StartupBackgroundServices}");
        }
        if (summary.PeriodicBackgroundServices > 0)
        {
            details.Add($"periodic={summary.PeriodicBackgroundServices}");
        }

        if (details.Count > 0)
        {
            segments.Add($"({string.Join(", ", details)})");
        }

        return string.Join(' ', segments);
    }

    private static string FormatInventoryHealth(HealthSnapshot? health)
    {
        if (health is null)
        {
            return "probes=0 overall=Unknown";
        }

        var total = health.Components.Count;
        var critical = health.Components.Count(sample => sample.Facts?.TryGetValue("critical", out var value) == true
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
        var optional = Math.Max(0, total - critical);
        var last = FormatRecency(health.ComputedAtUtc);

        var builder = new StringBuilder();
        builder.Append("probes=");
        builder.Append(total);

        if (total > 0)
        {
            builder.Append(" (");
            builder.Append("critical=");
            builder.Append(critical);
            if (optional > 0)
            {
                builder.Append(' ');
                builder.Append("optional=");
                builder.Append(optional);
            }
            builder.Append(')');
        }

        builder.Append(' ');
        builder.Append("last=");
        builder.Append(last);
        builder.Append(' ');
        builder.Append("overall=");
        builder.Append(health.Overall);

        return builder.ToString();
    }

    private static string FormatReadyHealth(HealthSnapshot? health)
    {
        if (health is null)
        {
            return "status=Unknown components=0";
        }

        return $"status={health.Overall} components={health.Components.Count} last={FormatRecency(health.ComputedAtUtc)}";
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "--";
        }

        var value = duration.Value;
        if (value <= TimeSpan.Zero)
        {
            return "0ms";
        }

        if (value.TotalMilliseconds < 1000)
        {
            return $"{value.TotalMilliseconds:F0}ms";
        }

        if (value.TotalSeconds < 60)
        {
            return $"{value.TotalSeconds:F2}s";
        }

        if (value.TotalMinutes < 60)
        {
            return $"{value.TotalMinutes:F2}m";
        }

        if (value.TotalHours < 24)
        {
            return $"{value.TotalHours:F2}h";
        }

        return $"{value.TotalDays:F1}d";
    }

    private static string FormatRecency(DateTimeOffset timestamp)
    {
        if (timestamp == DateTimeOffset.MinValue)
        {
            return "--";
        }

        var delta = DateTimeOffset.UtcNow - timestamp;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMilliseconds < 1000)
        {
            return $"{delta.TotalMilliseconds:F0}ms";
        }

        if (delta.TotalSeconds < 60)
        {
            return $"{delta.TotalSeconds:F1}s";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{delta.TotalMinutes:F1}m";
        }

        if (delta.TotalHours < 48)
        {
            return $"{delta.TotalHours:F1}h";
        }

        return $"{delta.TotalDays:F1}d";
    }
}

internal sealed class KoanConsoleBlockBuilder
{
    private readonly KoanLogStage _stage;
    private readonly string _title;
    private readonly int _width;
    private readonly List<string> _lines = new();
    private readonly string? _tokenOverride;

    public KoanConsoleBlockBuilder(KoanLogStage stage, string title, int width, string? tokenOverride = null)
    {
        _stage = stage;
        _title = title;
        _width = Math.Max(48, width);
        _tokenOverride = tokenOverride;
    }

    public KoanConsoleBlockBuilder AddLine(string? text)
    {
        _lines.Add(text ?? "");
        return this;
    }

    public string Build()
    {
        var innerWidth = _width - 3;
        var token = _tokenOverride ?? _stage.GetToken();
        var headerPrefix = $"┌─ {token} {_title} ";
        var headerPad = Math.Max(0, _width - headerPrefix.Length);
        var header = headerPrefix + new string('─', headerPad);

        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine(header);

        foreach (var line in _lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                builder.AppendLine("│");
                continue;
            }

            foreach (var segment in Wrap(line, innerWidth))
            {
                var padded = segment.Length < innerWidth ? segment.PadRight(innerWidth) : segment;
                builder.Append("│ ");
                builder.AppendLine(padded);
            }
        }

        builder.Append('└');
        builder.Append(new string('─', Math.Max(0, _width - 1)));
        return builder.ToString();
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield return "";
            yield break;
        }

        if (width <= 0)
        {
            yield return value;
            yield break;
        }

        for (var index = 0; index < value.Length; index += width)
        {
            var remaining = value.Length - index;
            var take = Math.Min(width, remaining);
            yield return value.Substring(index, take);
        }
    }
}