using System.Text.Json.Serialization;

namespace Koan.Packaging.Models;

internal sealed class ReleaseManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string BeforeCommit { get; init; }
    public required string SourceCommit { get; init; }
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
    public required string SourceCommit { get; init; }
    public Dictionary<string, string> Packages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
