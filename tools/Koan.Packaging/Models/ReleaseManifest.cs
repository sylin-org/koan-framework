using Koan.Packaging.Infrastructure;
using System.Text.Json.Serialization;

namespace Koan.Packaging.Models;

internal sealed class ReleaseManifest
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = PackagingConstants.ReleaseManifestSchema;
    public required string PreviousVersionCommit { get; init; }
    public required string SourceCommit { get; init; }
    public required string VersionCommit { get; init; }
    [JsonRequired]
    public bool IsLineageBootstrap { get; init; }
    public List<string> BreakingRoots { get; init; } = [];
    [JsonRequired]
    public List<string> SharedInputs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public List<ReleasePackage> Packages { get; init; } = [];
}

internal sealed class ReleasePackage
{
    public required string PackageId { get; init; }
    public required string Version { get; init; }
    public string? PreviousVersion { get; init; }
    public required string ProjectPath { get; init; }
    public required string Kind { get; init; }
    public required string Reason { get; init; }
    public List<string> BreakingRoots { get; init; } = [];
    public bool LineageMarkerGenerated { get; init; }
    public bool AlreadyPublished { get; set; }
    public bool IncludeSymbols { get; init; }
    public List<string> ProjectDependencies { get; init; } = [];
    public List<PackageDependency> PackageDependencies { get; set; } = [];
    public string? PackageFile { get; set; }
    public string? PackageSha256 { get; set; }
    public string? SymbolsFile { get; set; }
    public string? SymbolsSha256 { get; set; }

    [JsonIgnore]
    public string Identity => $"{PackageId}/{Version}";
}

internal sealed record PackageDependency(string PackageId, string VersionRange, string? MinimumVersion);

internal sealed class ReleaseState
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = PackagingConstants.ReleaseManifestSchema;
    public required string VersionCommit { get; init; }
    public Dictionary<string, string> Packages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ReleaseLineage
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = PackagingConstants.ReleaseLineageSchema;
    public required string PreviousSourceCommit { get; init; }
    public required string SourceCommit { get; init; }
    public required string PreviousVersionCommit { get; init; }
    public required string VersionCommit { get; init; }
    [JsonRequired]
    public bool IsBootstrap { get; init; }
    public List<string> BreakingRoots { get; init; } = [];
    [JsonRequired]
    public List<string> SharedInputs { get; init; } = [];
    public List<string> ClosurePackages { get; init; } = [];
    public List<string> MarkerPackages { get; init; } = [];
    public List<ReleaseLineageTrigger> Triggers { get; init; } = [];
    public List<ReleaseLineagePackage> Packages { get; init; } = [];
    public List<ReleaseLineagePackage> RetiredPackages { get; init; } = [];
}

internal sealed class ReleaseLineageState
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = PackagingConstants.ReleaseLineageSchema;
    public required string PreviousSourceCommit { get; init; }
    public required string SourceCommit { get; init; }
    public required string PreviousVersionCommit { get; init; }
    [JsonRequired]
    public bool IsBootstrap { get; init; }
    public List<string> BreakingRoots { get; init; } = [];
    [JsonRequired]
    public List<string> SharedInputs { get; init; } = [];
    public List<string> ClosurePackages { get; init; } = [];
    public List<string> MarkerPackages { get; init; } = [];
    public List<ReleaseLineageTrigger> Triggers { get; init; } = [];
    public List<ReleaseLineagePackage> Packages { get; init; } = [];
    public List<ReleaseLineagePackage> RetiredPackages { get; init; } = [];
}

internal sealed record ReleaseLineagePackage(string PackageId, string ProjectPath, string? Version)
{
    [JsonRequired]
    public List<string> SharedInputs { get; init; } = [];
}

internal sealed record ReleaseLineageTrigger(
    string PackageId,
    IReadOnlyList<string> BreakingRoots,
    IReadOnlyList<string> SharedInputs);

internal sealed class ReleaseLineageMarker
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = PackagingConstants.ReleaseLineageSchema;
    public required string SourceCommit { get; init; }
    public required string PackageId { get; init; }
    public List<string> BreakingRoots { get; init; } = [];
    public List<string> SharedInputs { get; init; } = [];
}
