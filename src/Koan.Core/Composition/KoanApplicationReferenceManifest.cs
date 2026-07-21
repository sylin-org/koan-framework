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

/// <summary>
/// A direct, build-observed Koan reference. <see cref="RawIdentity"/> preserves the application-authored
/// package/project evidence while <see cref="CanonicalIdentity"/> gives source and package builds one
/// component identity. Transitive dependencies never appear here.
/// </summary>
public sealed record KoanReferenceIntent(
    KoanReferenceKind Kind,
    string RawIdentity,
    string CanonicalIdentity);

/// <summary>An ordinary dependency edge observed by Koan's build assets.</summary>
public sealed record KoanActivationDependency(string Owner, string Dependency);

/// <summary>
/// Immutable build provenance for the current application. Pillars use this service to distinguish
/// direct application intent from assemblies that merely arrived transitively.
/// </summary>
public sealed class KoanApplicationReferenceManifest
{
    private const int CurrentSchema = 1;
    private static readonly IReadOnlyList<KoanReferenceIntent> EmptyReferences =
        Array.Empty<KoanReferenceIntent>();
    private static readonly IReadOnlyList<KoanActivationDependency> EmptyDependencies =
        Array.Empty<KoanActivationDependency>();

    private KoanApplicationReferenceManifest(
        bool isPresent,
        IReadOnlyList<KoanReferenceIntent> directReferences,
        IReadOnlyList<KoanActivationDependency> dependencies)
    {
        IsPresent = isPresent;
        DirectReferences = directReferences;
        Dependencies = dependencies;
    }

    /// <summary>
    /// Whether the application carried Koan's build-generated provenance resource. False means
    /// provenance is unknown, not that the application has no direct references.
    /// </summary>
    public bool IsPresent { get; }

    /// <summary>Direct Koan references, ordered by kind and then identity.</summary>
    public IReadOnlyList<KoanReferenceIntent> DirectReferences { get; }

    /// <summary>Ordinary dependency edges, ordered by owner and dependency.</summary>
    public IReadOnlyList<KoanActivationDependency> Dependencies { get; }

    /// <summary>Returns true when the manifest declares the exact direct reference.</summary>
    public bool Contains(KoanReferenceKind kind, string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        return DirectReferences.Any(reference =>
            reference.Kind == kind
            && (string.Equals(reference.RawIdentity, identity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(reference.CanonicalIdentity, identity, StringComparison.OrdinalIgnoreCase)));
    }

    internal static KoanApplicationReferenceManifest Load(Assembly? applicationAssembly)
    {
        if (applicationAssembly is null) return Unknown();

        using var stream = applicationAssembly.GetManifestResourceStream(
            Constants.Composition.ReferenceManifestResourceName);
        if (stream is null)
        {
            var manifestWasPromised = applicationAssembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Any(attribute =>
                    string.Equals(
                        attribute.Key,
                        Constants.Composition.ReferenceManifestRequiredMetadataName,
                        StringComparison.Ordinal)
                    && string.Equals(attribute.Value, "1", StringComparison.Ordinal));
            if (manifestWasPromised)
            {
                throw new InvalidDataException(
                    $"The application build promised the embedded {Constants.Composition.ReferenceManifestResourceName} resource, but it is missing. " +
                    "Clean and rebuild the application with the matching Sylin.Koan.Core package; do not continue with guessed activation.");
            }

            return Unknown();
        }

        using var reader = new StreamReader(stream);
        return Parse(reader);
    }

    internal static KoanApplicationReferenceManifest Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var references = new List<KoanReferenceIntent>();
        var dependencies = new List<KoanActivationDependency>();
        var lineNumber = 0;
        var schemaRead = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var value = line.Trim();
            if (value.Length == 0) continue;

            var parts = value.Split('|');
            if (!schemaRead)
            {
                if (parts.Length != 2
                    || !string.Equals(parts[0], "schema", StringComparison.Ordinal)
                    || !int.TryParse(parts[1], out var schema)
                    || schema != CurrentSchema)
                {
                    throw InvalidLine(lineNumber);
                }

                schemaRead = true;
                continue;
            }

            switch (parts[0])
            {
                case "schema":
                    throw InvalidLine(lineNumber);
                case "reference":
                {
                    if (parts.Length != 4) throw InvalidLine(lineNumber);
                    var kind = parts[1] switch
                    {
                        "package" => KoanReferenceKind.Package,
                        "project" => KoanReferenceKind.Project,
                        _ => throw InvalidLine(lineNumber),
                    };
                    var rawIdentity = parts[2].Trim();
                    var canonicalIdentity = parts[3].Trim();
                    if (!IsKoanIdentity(kind, rawIdentity)
                        || !IsCanonicalKoanIdentity(canonicalIdentity))
                    {
                        throw InvalidLine(lineNumber);
                    }

                    references.Add(new KoanReferenceIntent(kind, rawIdentity, canonicalIdentity));
                    break;
                }
                case "dependency":
                {
                    if (parts.Length != 3) throw InvalidLine(lineNumber);
                    var owner = parts[1].Trim();
                    var dependency = parts[2].Trim();
                    if (!IsCanonicalKoanIdentity(owner) || !IsCanonicalKoanIdentity(dependency))
                        throw InvalidLine(lineNumber);
                    dependencies.Add(new KoanActivationDependency(owner, dependency));
                    break;
                }
                default:
                    throw InvalidLine(lineNumber);
            }
        }

        if (!schemaRead) throw InvalidLine(Math.Max(lineNumber, 1));

        var ordered = references
            .DistinctBy(
                reference => $"{reference.Kind}:{reference.CanonicalIdentity}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.CanonicalIdentity, StringComparer.Ordinal)
            .ToArray();
        var orderedDependencies = dependencies
            .DistinctBy(
                dependency => $"{dependency.Owner}|{dependency.Dependency}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(dependency => dependency.Owner, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Dependency, StringComparer.Ordinal)
            .ToArray();
        return new KoanApplicationReferenceManifest(
            isPresent: true,
            new ReadOnlyCollection<KoanReferenceIntent>(ordered),
            new ReadOnlyCollection<KoanActivationDependency>(orderedDependencies));
    }

    private static KoanApplicationReferenceManifest Unknown() =>
        new(isPresent: false, EmptyReferences, EmptyDependencies);

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

    private static bool IsCanonicalKoanIdentity(string identity) =>
        identity.Equals("Sylin.Koan", StringComparison.OrdinalIgnoreCase)
        || identity.StartsWith("Sylin.Koan.", StringComparison.OrdinalIgnoreCase);

    private static InvalidDataException InvalidLine(int lineNumber) => new(
        $"The embedded {Constants.Composition.ReferenceManifestResourceName} resource is malformed at line {lineNumber}. " +
        "Rebuild the application with a matching Sylin.Koan.Core build target; do not edit the generated manifest.");
}
