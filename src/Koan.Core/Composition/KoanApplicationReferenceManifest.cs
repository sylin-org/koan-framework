using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Koan.Core.Infrastructure;

namespace Koan.Core.Composition;

/// <summary>How an application directly referenced a Koan component.</summary>
public enum KoanReferenceKind
{
    Package,
    Project,
}

/// <summary>A direct, build-observed Koan reference. Transitive dependencies never appear here.</summary>
public sealed record KoanReferenceIntent(KoanReferenceKind Kind, string Identity);

/// <summary>
/// Immutable build provenance for the current application. Pillars use this service to distinguish
/// direct application intent from assemblies that merely arrived transitively.
/// </summary>
public sealed class KoanApplicationReferenceManifest
{
    private static readonly IReadOnlyList<KoanReferenceIntent> EmptyReferences =
        Array.Empty<KoanReferenceIntent>();

    private KoanApplicationReferenceManifest(bool isPresent, IReadOnlyList<KoanReferenceIntent> directReferences)
    {
        IsPresent = isPresent;
        DirectReferences = directReferences;
    }

    /// <summary>
    /// Whether the application carried Koan's build-generated provenance resource. False means
    /// provenance is unknown, not that the application has no direct references.
    /// </summary>
    public bool IsPresent { get; }

    /// <summary>Direct Koan references, ordered by kind and then identity.</summary>
    public IReadOnlyList<KoanReferenceIntent> DirectReferences { get; }

    /// <summary>Returns true when the manifest declares the exact direct reference.</summary>
    public bool Contains(KoanReferenceKind kind, string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        return DirectReferences.Any(reference =>
            reference.Kind == kind
            && string.Equals(reference.Identity, identity, StringComparison.OrdinalIgnoreCase));
    }

    internal static KoanApplicationReferenceManifest Load(Assembly? applicationAssembly)
    {
        if (applicationAssembly is null) return Unknown();

        using var stream = applicationAssembly.GetManifestResourceStream(
            Constants.Composition.ReferenceManifestResourceName);
        if (stream is null) return Unknown();

        using var reader = new StreamReader(stream);
        return Parse(reader);
    }

    internal static KoanApplicationReferenceManifest Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var references = new List<KoanReferenceIntent>();
        var lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var value = line.Trim();
            if (value.Length == 0) continue;

            var separator = value.IndexOf('|');
            if (separator <= 0 || separator == value.Length - 1 || value.IndexOf('|', separator + 1) >= 0)
                throw InvalidLine(lineNumber);

            var kindToken = value[..separator];
            var identity = value[(separator + 1)..].Trim();
            var kind = kindToken switch
            {
                "package" => KoanReferenceKind.Package,
                "project" => KoanReferenceKind.Project,
                _ => throw InvalidLine(lineNumber),
            };

            if (!IsKoanIdentity(kind, identity)) throw InvalidLine(lineNumber);
            references.Add(new KoanReferenceIntent(kind, identity));
        }

        var ordered = references
            .DistinctBy(reference => $"{reference.Kind}:{reference.Identity}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.Identity, StringComparer.Ordinal)
            .ToArray();
        return new KoanApplicationReferenceManifest(
            isPresent: true,
            new ReadOnlyCollection<KoanReferenceIntent>(ordered));
    }

    private static KoanApplicationReferenceManifest Unknown() =>
        new(isPresent: false, EmptyReferences);

    private static bool IsKoanIdentity(KoanReferenceKind kind, string identity) => kind switch
    {
        KoanReferenceKind.Package => identity.Equals("Sylin.Koan", StringComparison.OrdinalIgnoreCase)
            || identity.StartsWith("Sylin.Koan.", StringComparison.OrdinalIgnoreCase),
        KoanReferenceKind.Project => identity.Equals("Koan", StringComparison.OrdinalIgnoreCase)
            || identity.StartsWith("Koan.", StringComparison.OrdinalIgnoreCase)
            || identity.Equals("Sylin.Koan", StringComparison.OrdinalIgnoreCase)
            || identity.StartsWith("Sylin.Koan.", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static InvalidDataException InvalidLine(int lineNumber) => new(
        $"The embedded {Constants.Composition.ReferenceManifestResourceName} resource is malformed at line {lineNumber}. " +
        "Rebuild the application with a matching Sylin.Koan.Core build target; do not edit the generated manifest.");
}
