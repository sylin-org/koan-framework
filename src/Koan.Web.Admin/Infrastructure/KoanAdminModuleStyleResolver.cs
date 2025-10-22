using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Koan.Admin.Contracts;
using Koan.Core.Modules.Pillars;
using PillarDescriptor = Koan.Core.Modules.Pillars.KoanPillarCatalog.PillarDescriptor;

namespace Koan.Web.Admin.Infrastructure;

internal static class KoanAdminModuleStyleResolver
{
    private const string StyleNotePrefix = "admin-style";
    private const string DefaultPillar = "General";
    private const string DefaultIcon = "🧩";
    private const string DefaultClassPrefix = "general";
    private static readonly string[] FallbackColors =
    {
        "#38bdf8",
        "#f97316",
        "#22c55e",
        "#ec4899",
        "#8b5cf6",
        "#facc15",
        "#06b6d4",
        "#64748b",
        "#14b8a6",
        "#f43f5e",
        "#c084fc",
        "#a855f7",
        "#0ea5e9",
        "#2563eb",
        "#dc2626",
        "#fde047"
    };
    public static KoanAdminModuleStyle Resolve(KoanAdminModuleManifest module)
    {
        if (module is null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        var fromNotes = ParseStyleFromNotes(module);
        return fromNotes ?? CreateFallback(module);
    }

    public static IReadOnlyList<KoanAdminModuleStyle> ResolveAll(IEnumerable<KoanAdminModuleManifest> modules)
    {
        return modules.Select(Resolve).ToList();
    }

    public static string BuildStylesheet(IEnumerable<KoanAdminModuleStyle> styles)
    {
        var byPillarClass = styles
            .GroupBy(style => style.PillarClass, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var byModuleClass = styles
            .GroupBy(style => style.ModuleClass, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder();
        builder.AppendLine("/* Koan Admin module visuals (generated) */");

        foreach (var pillar in byPillarClass.Values)
        {
            builder.AppendLine($".{pillar.PillarClass} {{ --pillar-color-hex: {pillar.ColorHex}; --pillar-color-rgb: {pillar.ColorRgb}; }}");
        }

        foreach (var module in byModuleClass.Values)
        {
            builder.AppendLine($".{module.ModuleClass} {{ --module-color-hex: {module.ColorHex}; --module-color-rgb: {module.ColorRgb}; }}");
        }

        return builder.ToString();
    }

    private static KoanAdminModuleStyle? ParseStyleFromNotes(KoanAdminModuleManifest module)
    {
        foreach (var note in module.Notes ?? Array.Empty<string>())
        {
            if (!TryExtractStylePayload(note, out var payload))
            {
                continue;
            }

            var tokens = Tokenize(payload)
                .Select(token => token.Split('=', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => Unquote(parts[1]), StringComparer.OrdinalIgnoreCase);

            var notePillarValue = GetTokenValue(tokens, "pillar");
            var notePillarCode = GetTokenValue(tokens, "pillar-code") ?? notePillarValue;
            var notePillarLabel = GetTokenValue(tokens, "pillar-label") ?? notePillarValue;

            PillarDescriptor? descriptor = TryResolveDescriptor(notePillarCode, notePillarLabel, module.Name, out var resolvedDescriptor)
                ? resolvedDescriptor
                : null;

            var pillarLabel = !string.IsNullOrWhiteSpace(notePillarLabel)
                ? notePillarLabel!
                : descriptor?.Label ?? DerivePillarFromName(module.Name);

            if (string.IsNullOrWhiteSpace(pillarLabel))
            {
                pillarLabel = DefaultPillar;
            }

            var pillarKey = ResolvePillarKey(descriptor, notePillarCode, pillarLabel);

            var pillarClass = tokens.TryGetValue("pillar-class", out var pillarClassToken) && !string.IsNullOrWhiteSpace(pillarClassToken)
                ? SanitizeClassName(pillarClassToken!, "pillar")
                : $"pillar-{pillarKey}";

            var moduleClass = tokens.TryGetValue("module-class", out var moduleClassToken) && !string.IsNullOrWhiteSpace(moduleClassToken)
                ? SanitizeClassName(moduleClassToken!, "module")
                : $"module-{pillarKey}";

            var color = descriptor?.ColorHex ?? PickFallbackColor(module.Name, pillarLabel);
            if (tokens.TryGetValue("color", out var providedColor) && TryNormalizeColor(providedColor, out var normalizedColor))
            {
                color = normalizedColor;
            }

            var icon = descriptor?.Icon ?? DefaultIcon;
            if (tokens.TryGetValue("icon", out var providedIcon) && !string.IsNullOrWhiteSpace(providedIcon))
            {
                icon = providedIcon;
            }

            return CreateStyle(module.Name, pillarLabel, pillarKey, pillarClass, moduleClass, color, icon);
        }

        return null;
    }

    private static bool TryExtractStylePayload(string note, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrWhiteSpace(note))
        {
            return false;
        }

        var trimmed = note.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = trimmed.IndexOf(']');
            if (closing <= 1)
            {
                return false;
            }

            var header = trimmed.Substring(1, closing - 1);
            if (!header.Equals(StyleNotePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            payload = trimmed[(closing + 1)..].TrimStart(':', ' ', '\t');
            return payload.Length > 0;
        }

        if (trimmed.StartsWith(StyleNotePrefix, StringComparison.OrdinalIgnoreCase))
        {
            payload = trimmed[StyleNotePrefix.Length..].TrimStart(':', ' ', '\t');
            return payload.Length > 0;
        }

        return false;
    }

    private static IEnumerable<string> Tokenize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        var current = new StringBuilder();
        bool inQuotes = false;
        char quoteChar = '\0';

        foreach (var ch in payload)
        {
            if (inQuotes)
            {
                if (ch == quoteChar)
                {
                    inQuotes = false;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch == '\"' || ch == '\'')
            {
                inQuotes = true;
                quoteChar = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string? GetTokenValue(IDictionary<string, string> tokens, string key)
    {
        if (tokens.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }

    private static string Unquote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static KoanAdminModuleStyle CreateFallback(KoanAdminModuleManifest module)
    {
        if (TryResolveDescriptor(null, null, module.Name, out var descriptor))
        {
            var pillarLabel = descriptor.Label;
            var pillarKey = ResolvePillarKey(descriptor, descriptor.Code, pillarLabel);
            var pillarClassKnown = $"pillar-{pillarKey}";
            var moduleClassKnown = $"module-{pillarKey}";
            return CreateStyle(module.Name, pillarLabel, pillarKey, pillarClassKnown, moduleClassKnown, descriptor.ColorHex, descriptor.Icon);
        }

        var pillarLabelFallback = DerivePillarFromName(module.Name);
        var pillarKeyFallback = ResolvePillarKey(null, null, pillarLabelFallback);
        var pillarClass = $"pillar-{pillarKeyFallback}";
        var moduleClass = $"module-{pillarKeyFallback}";
    var color = PickFallbackColor(module.Name, pillarLabelFallback);
        return CreateStyle(module.Name, pillarLabelFallback, pillarKeyFallback, pillarClass, moduleClass, color, DefaultIcon);
    }

    private static KoanAdminModuleStyle CreateStyle(string moduleName, string pillar, string pillarKey, string pillarClass, string moduleClass, string colorHex, string icon)
    {
        var rgb = ToRgb(colorHex);
        return new KoanAdminModuleStyle(moduleName, pillar, pillarKey, pillarClass, moduleClass, colorHex, rgb, icon);
    }

    private static bool TryResolveDescriptor(string? pillarCode, string? pillarLabel, string moduleName, out PillarDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(pillarCode) && KoanPillarCatalog.TryGetByCode(pillarCode, out descriptor))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(pillarLabel) && KoanPillarCatalog.TryGetByLabel(pillarLabel, out descriptor))
        {
            return true;
        }

        if (KoanPillarCatalog.TryMatchByModuleName(moduleName, out descriptor))
        {
            return true;
        }

        descriptor = default!;
        return false;
    }

    private static string DerivePillarFromName(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return DefaultPillar;
        }

        if (moduleName.StartsWith("Koan.", StringComparison.OrdinalIgnoreCase))
        {
            var segments = moduleName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                return Normalize(segments[1]);
            }
        }

        return DefaultPillar;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultPillar;
        }

        value = value.Replace('-', ' ');
        value = value.Replace('_', ' ');
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(value.ToLowerInvariant()).Replace(" ", string.Empty);
    }

    private static string ResolvePillarKey(PillarDescriptor? descriptor, string? requestedCode, string pillarLabel)
    {
        if (descriptor is not null && !string.IsNullOrWhiteSpace(descriptor.Code))
        {
            return ToKey(descriptor.Code);
        }

        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            return ToKey(requestedCode!);
        }

        return ToKey(pillarLabel);
    }

    private static bool TryNormalizeColor(string value, out string colorHex)
    {
        colorHex = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            var hex = trimmed[1..];
            if (hex.Length is 3 or 6)
            {
                if (hex.Length == 3)
                {
                    hex = string.Concat(hex.Select(ch => new string(ch, 2)));
                }

                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                {
                    colorHex = "#" + hex.ToLowerInvariant();
                    return true;
                }
            }

            return false;
        }

        if (trimmed.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = trimmed.IndexOf('(');
            var close = trimmed.IndexOf(')');
            if (open > 0 && close > open)
            {
                var parts = trimmed[(open + 1)..close]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => int.TryParse(p, out var component) ? Math.Clamp(component, 0, 255) : (int?)null)
                    .ToArray();

                if (parts.Length >= 3 && parts.All(p => p is not null))
                {
                    colorHex = $"#{parts[0]!.Value:X2}{parts[1]!.Value:X2}{parts[2]!.Value:X2}".ToLowerInvariant();
                    return true;
                }
            }
        }

        return false;
    }

    private static string PickFallbackColor(string moduleName, string seed)
    {
        if (string.IsNullOrEmpty(moduleName) && string.IsNullOrEmpty(seed))
        {
            return FallbackColors[0];
        }

        var hash = 17;
        var composite = string.Concat(moduleName ?? string.Empty, "|", seed ?? string.Empty);
        foreach (var ch in composite)
        {
            hash = unchecked(hash * 31 + ch);
        }

        var index = Math.Abs(hash) % FallbackColors.Length;
        return FallbackColors[index];
    }

    private static string ToRgb(string colorHex)
    {
        var hex = colorHex.TrimStart('#');
        if (hex.Length != 6)
        {
            return "56, 189, 248";
        }

        var r = int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return $"{r}, {g}, {b}";
    }

    private static string ToKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultClassPrefix;
        }

        var builder = new StringBuilder();
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        if (builder.Length == 0)
        {
            return DefaultClassPrefix;
        }

        return builder.ToString();
    }

    private static string SanitizeClassName(string value, string prefix)
    {
        var key = ToKey(value);
        if (string.IsNullOrEmpty(key))
        {
            return $"{prefix}-{DefaultClassPrefix}";
        }

        var prefixed = key.StartsWith("module-", StringComparison.OrdinalIgnoreCase) || key.StartsWith("pillar-", StringComparison.OrdinalIgnoreCase);
        if (prefixed)
        {
            return key;
        }

        return $"{prefix}-{key}";
    }
}

internal sealed record KoanAdminModuleStyle(
    string ModuleName,
    string Pillar,
    string PillarKey,
    string PillarClass,
    string ModuleClass,
    string ColorHex,
    string ColorRgb,
    string Icon
);
