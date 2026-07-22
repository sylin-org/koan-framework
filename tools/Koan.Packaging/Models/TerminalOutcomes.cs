using System.Text.Json.Serialization;

namespace Koan.Packaging.Models;

internal sealed class TerminalOutcomeCertificate
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    public string ArchitectureDecision { get; init; } = string.Empty;
    public List<RemovedOwnerOutcome> Outcomes { get; init; } = [];
}

internal sealed record RemovedOwnerOutcome
{
    public required string PackageId { get; init; }
    public required string Disposition { get; init; }
    public string? Destination { get; init; }
    public required string PublicCommit { get; init; }
    public List<string> Commands { get; init; } = [];
    public List<string> Evidence { get; init; } = [];
}

internal sealed record TerminalOutcomeReport(
    string ArchitectureDecision,
    int BaselineOwners,
    int ActiveSupported,
    int Removed,
    int Resolved,
    int Remaining,
    bool Final,
    IReadOnlyList<string> RemainingOwners);
