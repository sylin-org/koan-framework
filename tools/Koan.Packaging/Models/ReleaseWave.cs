using System.Text.Json.Serialization;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Models;

internal sealed record ReleaseWave
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = PackagingConstants.ReleaseWave.Schema;

    public required string Status { get; init; }
    public required string PreviousVersionCommit { get; init; }
    public required string SourceCommit { get; init; }
    public required string VersionCommit { get; init; }
    public required string TagName { get; init; }
    public required string TagCommit { get; init; }
    public required ReleaseWaveFile Bundle { get; init; }
    public required ReleaseWaveFile Lineage { get; init; }
    public required ReleaseWaveFile Manifest { get; init; }

    [JsonRequired]
    public int PackageCount { get; init; }

    public required ReleaseWaveProducer Producer { get; init; }
}

internal sealed record ReleaseWaveFile(string FileName, long Length, string Sha256);

internal sealed record ReleaseWaveProducer(
    string Name,
    string Version,
    string Runtime,
    string OperatingSystem);

internal sealed record ReleaseWavePreparation(
    string LineagePath,
    string ManifestPath,
    string ArtifactDirectory,
    string EvidenceDirectory,
    string OutputDirectory,
    ReleaseWaveProducer? Producer = null);
