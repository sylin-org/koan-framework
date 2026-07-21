using System.Text.Json.Serialization;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Models;

internal sealed class PackageQualityReport
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = PackagingConstants.PackageQuality.Schema;
    public required string Source { get; init; }
    public required PackageQualitySummary Summary { get; init; }
    public List<PackageQualityFinding> FindingDefinitions { get; init; } = [];
    public List<PackageQualityAssessment> Packages { get; init; } = [];
}

internal sealed record PackageQualitySummary(
    int Packages,
    int RepairRequired,
    int ReviewRequired,
    int StructurallyReady,
    int OwnedReadmes,
    int TechnicalCompanions,
    int Findings);

internal sealed record PackageQualityAssessment(
    string PackageId,
    string ProjectPath,
    string Shape,
    string Role,
    string Status,
    string Description,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> Dependencies,
    string? Readme,
    string? TechnicalDocumentation,
    string? PackageIcon,
    IReadOnlyList<string> Findings);

internal sealed record PackageQualityFinding(
    string Code,
    string Severity,
    string Summary,
    string Correction);
