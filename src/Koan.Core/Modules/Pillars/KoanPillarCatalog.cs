using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Core.Modules.Pillars;

public static class KoanPillarCatalog
{
    private static readonly ConcurrentDictionary<string, PillarDescriptor> PillarsByCode = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> PillarCodesByNormalizedLabel = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> PillarCodesByNamespacePrefix = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<PillarDescriptor> All
        => PillarsByCode.Values
            .OrderBy(static descriptor => descriptor.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsRegistered(string? pillarCode)
        => !string.IsNullOrWhiteSpace(pillarCode) && PillarsByCode.ContainsKey(pillarCode);

    public static PillarDescriptor RegisterDescriptor(PillarDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var stored = PillarsByCode.AddOrUpdate(
            descriptor.Code,
            static (_, incoming) => incoming,
            static (_, existing, incoming) => MergeDescriptors(existing, incoming),
            descriptor);

        PillarCodesByNormalizedLabel.AddOrUpdate(
            stored.Label,
            _ => stored.Code,
            (_, existingCode) =>
            {
                if (!string.Equals(existingCode, stored.Code, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Pillar label '{stored.Label}' is already bound to '{existingCode}'.");
                }

                return existingCode;
            });

        foreach (var prefix in descriptor.NamespacePrefixes)
        {
            AssociateNamespaceInternal(stored, prefix);
        }

        return stored;
    }

    public static void AssociateNamespace(string pillarCode, string namespacePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pillarCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespacePrefix);

        if (!TryGetByCode(pillarCode, out var descriptor))
        {
            throw new InvalidOperationException($"Pillar '{pillarCode}' has not been registered.");
        }

        AssociateNamespaceInternal(descriptor, namespacePrefix);
    }

    public static bool TryGetByCode(string? code, out PillarDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            descriptor = default!;
            return false;
        }

        if (PillarsByCode.TryGetValue(code.Trim(), out var found))
        {
            descriptor = found;
            return true;
        }

        descriptor = default!;
        return false;
    }

    public static PillarDescriptor RequireByCode(string code)
    {
        if (!TryGetByCode(code, out var descriptor))
        {
            throw new InvalidOperationException($"Pillar '{code}' has not been registered.");
        }

        return descriptor;
    }

    public static bool TryGetByLabel(string? label, out PillarDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            descriptor = default!;
            return false;
        }

        if (PillarCodesByNormalizedLabel.TryGetValue(label, out var code)
            && PillarsByCode.TryGetValue(code, out var byLabel))
        {
            descriptor = byLabel;
            return true;
        }

        foreach (var candidate in PillarsByCode.Values)
        {
            if (string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase))
            {
                descriptor = candidate;
                return true;
            }
        }

        descriptor = default!;
        return false;
    }

    public static bool TryMatchByModuleName(string? moduleName, out PillarDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            descriptor = default!;
            return false;
        }

        var prefixes = PillarCodesByNamespacePrefix.Keys.ToArray();
        Array.Sort(prefixes, (a, b) => b.Length.CompareTo(a.Length));

        foreach (var prefix in prefixes)
        {
            if (!moduleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (PillarCodesByNamespacePrefix.TryGetValue(prefix, out var code)
                && PillarsByCode.TryGetValue(code, out var matched))
            {
                descriptor = matched;
                return true;
            }
        }

        descriptor = default!;
        return false;
    }

    private static PillarDescriptor MergeDescriptors(PillarDescriptor existing, PillarDescriptor incoming)
    {
        if (!existing.SemanticallyEquals(incoming))
        {
            throw new InvalidOperationException($"Pillar '{existing.Code}' is already registered with different metadata.");
        }

        foreach (var prefix in incoming.NamespacePrefixes)
        {
            existing.AddNamespacePrefix(prefix);
        }

        return existing;
    }

    private static void AssociateNamespaceInternal(PillarDescriptor descriptor, string namespacePrefix)
    {
        var normalized = PillarDescriptor.NormalizeNamespacePrefix(namespacePrefix);
        descriptor.AddNamespacePrefix(normalized);

        PillarCodesByNamespacePrefix.AddOrUpdate(
            normalized,
            _ => descriptor.Code,
            (_, existingCode) =>
            {
                if (!string.Equals(existingCode, descriptor.Code, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Namespace prefix '{normalized}' is already bound to pillar '{existingCode}'.");
                }

                return existingCode;
            });
    }

    public sealed class PillarDescriptor : IEquatable<PillarDescriptor>
    {
        private readonly ConcurrentDictionary<string, byte> _namespacePrefixes;

        public PillarDescriptor(string code, string label, string colorHex, string icon, IEnumerable<string>? namespacePrefixes = null)
        {
            Code = string.IsNullOrWhiteSpace(code)
                ? throw new ArgumentException("Pillar code is required.", nameof(code))
                : code.Trim();
            Label = string.IsNullOrWhiteSpace(label)
                ? throw new ArgumentException("Pillar label is required.", nameof(label))
                : label.Trim();
            Icon = string.IsNullOrWhiteSpace(icon)
                ? throw new ArgumentException("Pillar icon is required.", nameof(icon))
                : icon;
            ColorHex = NormalizeColor(colorHex);

            _namespacePrefixes = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            if (namespacePrefixes is not null)
            {
                foreach (var prefix in namespacePrefixes)
                {
                    var normalized = NormalizeNamespacePrefix(prefix);
                    if (normalized.Length == 0)
                    {
                        continue;
                    }

                    _namespacePrefixes.TryAdd(normalized, 0);
                }
            }
        }

        public string Code { get; }

        public string Label { get; }

        public string ColorHex { get; }

        public string Icon { get; }

        public IReadOnlyCollection<string> NamespacePrefixes => _namespacePrefixes.Keys.ToArray();

        internal void AddNamespacePrefix(string prefix)
        {
            var normalized = NormalizeNamespacePrefix(prefix);
            if (normalized.Length == 0)
            {
                return;
            }

            _namespacePrefixes.TryAdd(normalized, 0);
        }

        public bool Equals(PillarDescriptor? other)
        {
            if (other is null)
            {
                return false;
            }

            return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Label, other.Label, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(ColorHex, other.ColorHex, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Icon, other.Icon, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is PillarDescriptor other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Code, StringComparer.OrdinalIgnoreCase);
            hash.Add(Label, StringComparer.OrdinalIgnoreCase);
            hash.Add(ColorHex, StringComparer.OrdinalIgnoreCase);
            hash.Add(Icon, StringComparer.Ordinal);
            return hash.ToHashCode();
        }

        internal bool SemanticallyEquals(PillarDescriptor other) => Equals(other);

        internal static string NormalizeNamespacePrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed;
        }

        private static string NormalizeColor(string value)
        {
            var hex = string.IsNullOrWhiteSpace(value) ? "#38bdf8" : value.Trim();
            if (!hex.StartsWith('#'))
            {
                hex = "#" + hex;
            }

            if (hex.Length == 4)
            {
                hex = "#" + string.Concat(hex[1..].Select(c => new string(c, 2))).ToLowerInvariant();
            }

            return hex.Length == 7 ? hex.ToLowerInvariant() : hex;
        }
    }
}
