using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Koan.Testing.Conformance.Infrastructure;

namespace Koan.Testing;

/// <summary>
/// The immutable executable projection generated from the primer. The primer remains semantic authority; this catalog
/// supplies stable test metadata and rejects a malformed or incomplete generated resource at first use.
/// </summary>
public static class DataConformanceCatalog
{
    /// <summary>One stable acceptance obligation and its conjunctive evidence requirements.</summary>
    public sealed record Cell(
        string Id,
        IReadOnlyList<DataConformanceEvidenceKind> Evidence,
        string Requirement,
        string Verifier);

    /// <summary>One deterministic applicability rule and the acceptance cells it selects.</summary>
    public sealed record Profile(
        string Id,
        string Applicability,
        IReadOnlyList<string> AcceptanceIds);

    private sealed class CatalogDocument
    {
        public int SchemaVersion { get; init; }
        public string ProtocolVersion { get; init; } = string.Empty;
        public string PrimerPath { get; init; } = string.Empty;
        public string PrimerSha256 { get; init; } = string.Empty;
        public List<CellDocument> Cells { get; init; } = [];
        public List<ProfileDocument> Profiles { get; init; } = [];
    }

    private sealed class CellDocument
    {
        public string Id { get; init; } = string.Empty;
        public List<string> Evidence { get; init; } = [];
        public string Requirement { get; init; } = string.Empty;
        public string Verifier { get; init; } = string.Empty;
    }

    private sealed class ProfileDocument
    {
        public string Id { get; init; } = string.Empty;
        public string Applicability { get; init; } = string.Empty;
        public List<string> AcceptanceIds { get; init; } = [];
    }

    private static readonly IReadOnlyDictionary<string, Cell> CellsById;
    private static readonly IReadOnlyDictionary<string, Profile> ProfilesById;

    static DataConformanceCatalog()
    {
        var assembly = typeof(DataConformanceCatalog).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(name =>
            name.EndsWith(DataConformanceConstants.CatalogResourceSuffix, StringComparison.Ordinal));
        if (resource is null)
        {
            throw new InvalidOperationException(
                $"Embedded Data conformance catalog '{DataConformanceConstants.CatalogResourceSuffix}' is missing.");
        }

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded Data conformance catalog '{resource}' cannot be read.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        CatalogSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var document = JsonSerializer.Deserialize<CatalogDocument>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("Embedded Data conformance catalog is empty.");
        if (document.SchemaVersion != DataConformanceConstants.SchemaVersion ||
            !string.Equals(document.ProtocolVersion, DataConformanceConstants.ProtocolVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Data conformance catalog protocol '{document.ProtocolVersion}'/schema {document.SchemaVersion} is unsupported.");
        }

        PrimerPath = RequireText(document.PrimerPath, "primer path");
        PrimerSha256 = RequireFingerprint(document.PrimerSha256, "primer fingerprint");
        ProtocolVersion = document.ProtocolVersion;

        var cells = document.Cells.Select(ToCell).OrderBy(cell => cell.Id, StringComparer.Ordinal).ToArray();
        if (cells.Length != DataConformanceConstants.ExpectedCellCount)
        {
            throw new InvalidOperationException(
                $"Data conformance catalog contains {cells.Length} cells; expected {DataConformanceConstants.ExpectedCellCount}.");
        }
        CellsById = Unique(cells, cell => cell.Id, "acceptance cell");

        var profiles = document.Profiles.Select(ToProfile).OrderBy(profile => profile.Id, StringComparer.Ordinal).ToArray();
        if (profiles.Length != DataConformanceConstants.ExpectedProfileCount)
        {
            throw new InvalidOperationException(
                $"Data conformance catalog contains {profiles.Length} profiles; expected {DataConformanceConstants.ExpectedProfileCount}.");
        }
        ProfilesById = Unique(profiles, profile => profile.Id, "conformance profile");
        foreach (var profile in profiles)
        {
            foreach (var acceptanceId in profile.AcceptanceIds)
            {
                if (!CellsById.ContainsKey(acceptanceId))
                {
                    throw new InvalidOperationException(
                        $"Data conformance profile '{profile.Id}' references unknown cell '{acceptanceId}'.");
                }
            }
        }

        Cells = cells;
        Profiles = profiles;
    }

    /// <summary>The generated protocol identity.</summary>
    public static string ProtocolVersion { get; }

    /// <summary>The source primer's repository-relative path.</summary>
    public static string PrimerPath { get; }

    /// <summary>The exact primer fingerprint used to generate this catalog.</summary>
    public static string PrimerSha256 { get; }

    /// <summary>The exact embedded catalog fingerprint carried by every packet.</summary>
    public static string CatalogSha256 { get; }

    /// <summary>All stable acceptance cells, ordered by ID.</summary>
    public static IReadOnlyList<Cell> Cells { get; }

    /// <summary>All applicability profiles, ordered by name.</summary>
    public static IReadOnlyList<Profile> Profiles { get; }

    /// <summary>Resolve one stable acceptance cell or fail with a corrective message.</summary>
    public static Cell Acceptance(string id) =>
        CellsById.TryGetValue(RequireText(id, "acceptance ID"), out var cell)
            ? cell
            : throw new ArgumentException($"Unknown Data conformance acceptance ID '{id}'.", nameof(id));

    /// <summary>Resolve one profile or fail with the available choices.</summary>
    public static Profile ResolveProfile(string id) =>
        ProfilesById.TryGetValue(RequireText(id, "profile ID"), out var profile)
            ? profile
            : throw new ArgumentException(
                $"Unknown Data conformance profile '{id}'. Available profiles: {string.Join(", ", ProfilesById.Keys)}.",
                nameof(id));

    private static Cell ToCell(CellDocument value)
    {
        var id = RequireText(value.Id, "acceptance ID");
        var evidence = value.Evidence.Select(ParseEvidence).Distinct().ToArray();
        if (evidence.Length == 0) throw new InvalidOperationException($"Acceptance cell '{id}' has no evidence kinds.");
        return new Cell(
            id,
            evidence,
            RequireText(value.Requirement, $"requirement for '{id}'"),
            RequireText(value.Verifier, $"verifier for '{id}'"));
    }

    private static Profile ToProfile(ProfileDocument value)
    {
        var id = RequireText(value.Id, "profile ID");
        var acceptanceIds = value.AcceptanceIds.Select(item => RequireText(item, $"acceptance ID in '{id}'"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (acceptanceIds.Length == 0) throw new InvalidOperationException($"Conformance profile '{id}' selects no cells.");
        return new Profile(id, RequireText(value.Applicability, $"applicability for '{id}'"), acceptanceIds);
    }

    private static DataConformanceEvidenceKind ParseEvidence(string value) => value switch
    {
        "STATIC" => DataConformanceEvidenceKind.Static,
        "BOOT" => DataConformanceEvidenceKind.Boot,
        "ORACLE" => DataConformanceEvidenceKind.Oracle,
        "LIVE" => DataConformanceEvidenceKind.Live,
        "NEG" => DataConformanceEvidenceKind.Negative,
        "FAULT" => DataConformanceEvidenceKind.Fault,
        "PLAN" => DataConformanceEvidenceKind.Plan,
        "LIFE" => DataConformanceEvidenceKind.Lifecycle,
        "PERF" => DataConformanceEvidenceKind.Performance,
        _ => throw new InvalidOperationException($"Unknown Data conformance evidence kind '{value}'."),
    };

    private static IReadOnlyDictionary<string, T> Unique<T>(
        IEnumerable<T> values,
        Func<T, string> key,
        string kind)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var id = key(value);
            if (!result.TryAdd(id, value)) throw new InvalidOperationException($"Duplicate {kind} '{id}'.");
        }
        return result;
    }

    private static string RequireFingerprint(string? value, string field)
    {
        var text = RequireText(value, field);
        return text.Length == 64 && text.All(Uri.IsHexDigit)
            ? text.ToLowerInvariant()
            : throw new InvalidOperationException($"Data conformance {field} must be a SHA-256 value.");
    }

    private static string RequireText(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Data conformance {field} is required.")
            : value.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}
