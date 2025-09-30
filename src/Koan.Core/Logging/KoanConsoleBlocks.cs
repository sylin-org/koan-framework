using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Observability.Health;

namespace Koan.Core.Logging;

internal static class KoanConsoleBlocks
{
    private const int DefaultWidth = 78;
    private const int LabelWidth = 14;

    public static string BuildBootstrapHeaderBlock(KoanEnvironmentSnapshot snapshot, string hostDescription, IReadOnlyList<(string Name, string Version)> modules, string runtimeVersion)
    {
        var uniqueModules = DeduplicateModules(modules);

        var builder = new KoanConsoleBlockBuilder(KoanLogStage.Boot, "Koan Bootstrap", DefaultWidth)
            .AddLine(FormatKeyValue("Runtime", $"Koan.Core {runtimeVersion}"))
            .AddLine(FormatKeyValue("Host", hostDescription))
            .AddLine(FormatKeyValue("Modules", uniqueModules.Count.ToString()))
            .AddLine(FormatKeyValue("Session", snapshot.SessionId))
            .AddLine(FormatKeyValue("Timestamp", DateTimeOffset.UtcNow.ToString("o")))
            .AddLine(FormatKeyValue("Orchestration", snapshot.OrchestrationMode.ToString()));

        return builder.Build();
    }

    public static string BuildInventoryBlock(KoanEnvironmentSnapshot snapshot, IReadOnlyList<(string Name, string Version)> modules)
    {
        var uniqueModules = DeduplicateModules(modules);
        var builder = new KoanConsoleBlockBuilder(KoanLogStage.Snap, "Koan Inventory", DefaultWidth);

        foreach (var module in uniqueModules)
        {
            builder.AddLine(FormatKeyValue(module.Name, module.Version));
        }

        if (uniqueModules.Count > 0)
        {
            builder.AddLine(string.Empty);
        }

        builder
            .AddLine(FormatKeyValue("Environment", $"{snapshot.EnvironmentName} ({snapshot.OrchestrationMode})"))
            .AddLine(FormatKeyValue("InContainer", snapshot.InContainer ? "true" : "false"))
            .AddLine(FormatKeyValue("Process", $"Started {snapshot.ProcessStart:o}"))
            .AddLine(FormatKeyValue("Uptime", FormatUptime(snapshot.ProcessStart)))
            .AddLine(FormatKeyValue("Machine", Environment.MachineName))
            .AddLine(FormatKeyValue("Session", snapshot.SessionId));

        return builder.Build();
    }

    public static string BuildReadyBlock(IEnumerable<string> urls, StartupTimelineSummary timeline, HealthSnapshot? health)
    {
        var addressList = urls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        var urlString = addressList.Count > 0 ? string.Join(", ", addressList) : "(not published)";
        var timing = FormatTimeline(timeline);
        var healthLine = health is null
            ? "status=unknown"
            : $"status={health.Overall} contributors={health.Components.Count}";

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

    private static string FormatUptime(DateTimeOffset start)
    {
        var uptime = DateTimeOffset.UtcNow - start;
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }
        return uptime.ToString();
    }

    private static string FormatTimeline(StartupTimelineSummary summary)
    {
        var segments = new List<string>();
        if (summary.Boot is TimeSpan boot)
        {
            segments.Add($"boot={boot.TotalMilliseconds:F0}ms");
        }
        if (summary.Config is TimeSpan config)
        {
            segments.Add($"config={config.TotalMilliseconds:F0}ms");
        }
        if (summary.Data is TimeSpan data)
        {
            segments.Add($"data={data.TotalMilliseconds:F0}ms");
        }
        if (summary.Services is TimeSpan services)
        {
            segments.Add($"services={services.TotalMilliseconds:F0}ms");
        }
        if (summary.Total is TimeSpan total)
        {
            segments.Add($"total={total.TotalMilliseconds:F0}ms");
        }

        return segments.Count == 0 ? "(timing unavailable)" : string.Join(' ', segments);
    }
}

internal sealed class KoanConsoleBlockBuilder
{
    private readonly KoanLogStage _stage;
    private readonly string _title;
    private readonly int _width;
    private readonly List<string> _lines = new();

    public KoanConsoleBlockBuilder(KoanLogStage stage, string title, int width)
    {
        _stage = stage;
        _title = title;
        _width = Math.Max(48, width);
    }

    public KoanConsoleBlockBuilder AddLine(string? text)
    {
        _lines.Add(text ?? string.Empty);
        return this;
    }

    public string Build()
    {
        var innerWidth = _width - 3;
        var token = _stage.GetToken();
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
            yield return string.Empty;
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