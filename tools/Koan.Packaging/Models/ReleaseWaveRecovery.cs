using System.Text.Json.Serialization;

namespace Koan.Packaging.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ReleaseWaveEscrowState>))]
internal enum ReleaseWaveEscrowState
{
    [JsonStringEnumMemberName("missing")]
    Missing,

    [JsonStringEnumMemberName("staging")]
    Staging,

    [JsonStringEnumMemberName("prepared")]
    Prepared,

    [JsonStringEnumMemberName("published")]
    Published
}

internal sealed record ReleaseWaveInspection(
    ReleaseWaveEscrowState State,
    string TagName,
    string VersionCommit,
    string? ReleaseId);

internal sealed record ReleaseWaveEscrowRelease(
    string Id,
    string TagName,
    string TargetCommit,
    bool IsDraft,
    bool IsImmutable);

internal sealed record ReleaseWaveEscrowAsset(string Name, long Length, bool IsUploaded);

internal sealed record ReleaseWaveCompletion
{
    public const int CurrentSchema = 1;
    public const string FileName = "release-completion.json";
    public const string CompleteStatus = "complete";
    public const string AvailableDisposition = "available";
    public const string SymbolsAcceptedDisposition = "accepted-or-duplicate";
    public const string SymbolsNotRequiredDisposition = "not-required";

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchema;

    public required string Status { get; init; }
    public required string PreviousVersionCommit { get; init; }
    public required string SourceCommit { get; init; }
    public required string VersionCommit { get; init; }
    public required string TagName { get; init; }
    public required string TagCommit { get; init; }
    public required ReleaseWaveFile PreparedMarker { get; init; }
    public required ReleaseWaveFile Bundle { get; init; }
    public required ReleaseWaveFile Lineage { get; init; }
    public required ReleaseWaveFile Manifest { get; init; }

    [JsonRequired]
    public int PackageCount { get; init; }

    public List<ReleaseWavePackageCompletion> Packages { get; init; } = [];
}

internal sealed record ReleaseWavePackageCompletion
{
    public required string PackageId { get; init; }
    public required string Version { get; init; }
    public required ReleaseWaveFile Package { get; init; }
    public required string PackageDisposition { get; init; }
    public ReleaseWaveFile? Symbols { get; init; }
    public required string SymbolsDisposition { get; init; }
}
