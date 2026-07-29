using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Koan.Testing.Conformance.Infrastructure;

namespace Koan.Testing;

/// <summary>
/// A deterministic, self-validating Data adapter evidence packet. Rows are compiled from one manifest and the generated
/// primer catalog; verdicts are computed from evidence rather than supplied by callers.
/// </summary>
public sealed class DataConformancePacket
{
    public enum EvidenceOutcome
    {
        Passed,
        Failed,
        Deferred,
        Infrastructure,
    }

    public enum RowVerdict
    {
        Pass,
        Red,
        Deferred,
    }

    public enum DependencyKind
    {
        Owner,
        SourcePath,
        Tool,
        Profile,
        Fixture,
    }

    public enum ValidationStatus
    {
        Pass,
        Red,
        Deferred,
        Error,
        Infrastructure,
    }

    public sealed record Identity(
        string SourceFingerprint,
        string Provider,
        string Driver,
        string Fixture);

    public sealed record Evidence(
        string Id,
        string RowId,
        DataConformanceEvidenceKind Kind,
        EvidenceOutcome Outcome,
        string Command,
        string Artifact);

    public sealed record Dependency(
        DependencyKind Kind,
        string Id,
        string Fingerprint);

    public sealed record DependencyChange(
        DependencyKind Kind,
        string Id,
        string Fingerprint);

    public sealed record RowPlan(
        string AcceptanceId,
        string Case,
        string Owner,
        IReadOnlyList<string> ClaimReferences,
        IReadOnlyList<string>? LinkedRows = null,
        string? Blocker = null);

    public sealed record Row(
        string Id,
        string AcceptanceId,
        string Case,
        string Owner,
        IReadOnlyList<string> ClaimReferences,
        IReadOnlyList<string> LinkedRows,
        IReadOnlyList<DataConformanceEvidenceKind> RequiredEvidence,
        IReadOnlyList<string> EvidenceReferences,
        RowVerdict Verdict,
        string? Blocker);

    public sealed record ValidationIssue(string Code, string Message);

    public sealed record ValidationResult(ValidationStatus Status, IReadOnlyList<ValidationIssue> Issues)
    {
        public bool IsValid => Status == ValidationStatus.Pass;

        public int ExitCode => Status switch
        {
            ValidationStatus.Pass => 0,
            ValidationStatus.Red => 1,
            ValidationStatus.Deferred => 2,
            ValidationStatus.Error => 3,
            ValidationStatus.Infrastructure => 4,
            _ => 3,
        };
    }

    public int SchemaVersion { get; init; } = DataConformanceConstants.SchemaVersion;
    public string ProtocolVersion { get; init; } = DataConformanceConstants.ProtocolVersion;
    public string PrimerSha256 { get; init; } = DataConformanceCatalog.PrimerSha256;
    public string CatalogSha256 { get; init; } = DataConformanceCatalog.CatalogSha256;
    public string Adapter { get; init; } = string.Empty;
    public Identity Source { get; init; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
    public IReadOnlyList<DataConformanceManifest.Claim> Claims { get; init; } = [];
    public IReadOnlyList<Row> Rows { get; init; } = [];
    public IReadOnlyList<Evidence> EvidenceItems { get; init; } = [];
    public IReadOnlyList<Dependency> Dependencies { get; init; } = [];

    /// <summary>Compile one byte-stable packet from claims, row cases, evidence, and consumed dependencies.</summary>
    public static DataConformancePacket Compile(
        DataConformanceManifest manifest,
        Identity source,
        IEnumerable<Evidence>? evidence = null,
        IEnumerable<Dependency>? dependencies = null,
        IEnumerable<RowPlan>? rowPlans = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(source);
        ValidateIdentity(source);

        var evidenceItems = (evidence ?? []).Select(Normalize).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        EnsureUnique(evidenceItems.Select(item => item.Id), "evidence ID");
        var positiveClaims = manifest.Claims.Where(claim => claim.Scope != DataConformanceManifest.ClaimScope.Declined)
            .ToArray();
        var selectedByAcceptance = positiveClaims
            .SelectMany(claim => DataConformanceCatalog.ResolveProfile(claim.ProfileId).AcceptanceIds
                .Select(acceptanceId => (AcceptanceId: acceptanceId, Claim: claim)))
            .GroupBy(item => item.AcceptanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Claim).ToArray(), StringComparer.Ordinal);

        var plans = (rowPlans ?? []).Select(Normalize).ToList();
        foreach (var (acceptanceId, claims) in selectedByAcceptance.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var supplied = plans.Where(plan => string.Equals(plan.AcceptanceId, acceptanceId, StringComparison.Ordinal)).ToArray();
            var linked = supplied.SelectMany(plan => plan.ClaimReferences).ToHashSet(StringComparer.Ordinal);
            foreach (var claim in claims.Where(claim => !linked.Contains(claim.Reference)))
            {
                plans.Add(new RowPlan(
                    acceptanceId,
                    DataConformanceConstants.DefaultCase,
                    claim.Owner,
                    [claim.Reference]));
            }
        }

        var mergedPlans = plans
            .GroupBy(plan => RowIdentity(plan.AcceptanceId, plan.Case, plan.Owner), StringComparer.Ordinal)
            .Select(group => new RowPlan(
                group.First().AcceptanceId,
                group.First().Case,
                group.First().Owner,
                group.SelectMany(plan => plan.ClaimReferences).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                group.SelectMany(plan => plan.LinkedRows ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                MergeBlocker(group.Select(plan => plan.Blocker))))
            .OrderBy(plan => RowIdentity(plan.AcceptanceId, plan.Case, plan.Owner), StringComparer.Ordinal)
            .ToArray();

        var rows = mergedPlans.Select(plan => CompileRow(plan, evidenceItems)).ToArray();
        var dependencyItems = (dependencies ?? []).Select(Normalize).ToList();
        dependencyItems.Add(new Dependency(DependencyKind.Profile, DataConformanceConstants.ProtocolVersion,
            DataConformanceCatalog.CatalogSha256));
        dependencyItems.Add(new Dependency(DependencyKind.Tool, "Koan.Testing",
            typeof(DataConformancePacket).Assembly.GetName().Version?.ToString() ?? "0.0.0.0"));
        dependencyItems.Add(new Dependency(DependencyKind.Fixture, source.Fixture, source.Fixture));
        var normalizedDependencies = dependencyItems.Distinct().OrderBy(item => item.Kind).ThenBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Fingerprint, StringComparer.Ordinal).ToArray();

        return new DataConformancePacket
        {
            Adapter = manifest.Adapter,
            Source = source,
            Claims = manifest.Claims.OrderBy(claim => claim.Reference, StringComparer.Ordinal).ToArray(),
            Rows = rows,
            EvidenceItems = evidenceItems,
            Dependencies = normalizedDependencies,
        };
    }

    /// <summary>Serialize with deterministic ordering and a terminal newline.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine;

    /// <summary>Read a packet without trusting its embedded verdicts or protocol identity.</summary>
    public static DataConformancePacket FromJson(string json) =>
        JsonSerializer.Deserialize<DataConformancePacket>(RequireText(json, "packet JSON"), JsonOptions)
        ?? throw new InvalidOperationException("Data conformance packet JSON is empty.");

    /// <summary>Validate protocol identity, selection completeness, evidence references, and mechanical verdicts.</summary>
    public ValidationResult Validate()
    {
        var issues = new List<ValidationIssue>();
        var structural = false;
        var falseClaim = false;
        var hasRed = false;
        var hasDeferred = false;
        var hasInfrastructure = false;

        void Error(string code, string message) { structural = true; issues.Add(new(code, message)); }
        void Red(string code, string message) { hasRed = true; issues.Add(new(code, message)); }

        if (SchemaVersion != DataConformanceConstants.SchemaVersion) Error("schema", $"Unsupported schema {SchemaVersion}.");
        if (!string.Equals(ProtocolVersion, DataConformanceCatalog.ProtocolVersion, StringComparison.Ordinal))
            Error("protocol", $"Packet protocol '{ProtocolVersion}' is stale.");
        if (!string.Equals(PrimerSha256, DataConformanceCatalog.PrimerSha256, StringComparison.Ordinal))
            Error("primer-fingerprint", "Packet primer fingerprint is stale.");
        if (!string.Equals(CatalogSha256, DataConformanceCatalog.CatalogSha256, StringComparison.Ordinal))
            Error("catalog-fingerprint", "Packet catalog fingerprint is stale.");
        if (string.IsNullOrWhiteSpace(Adapter)) Error("adapter", "Packet adapter ID is missing.");
        try { ValidateIdentity(Source); }
        catch (Exception exception) { Error("identity", exception.Message); }

        var claimByReference = IndexUnique(Claims, claim => claim.Reference, "claim", Error);
        var evidenceById = IndexUnique(EvidenceItems, item => item.Id, "evidence", Error);
        var rowById = IndexUnique(Rows, row => row.Id, "row", Error);

        foreach (var claim in Claims)
        {
            DataConformanceCatalog.Profile profile;
            try { profile = DataConformanceCatalog.ResolveProfile(claim.ProfileId); }
            catch (Exception exception) { Error("unknown-profile", exception.Message); continue; }

            if (claim.Scope == DataConformanceManifest.ClaimScope.Declined)
            {
                if (claim.Publication != DataConformanceManifest.ClaimPublication.Unadvertised)
                    Error("declined-advertised", $"Declined claim '{claim.Reference}' is advertised.");
                var correction = claim.CorrectiveEvidenceIds
                    .Where(evidenceById.ContainsKey)
                    .Select(id => evidenceById[id])
                    .ToArray();
                foreach (var missing in claim.CorrectiveEvidenceIds.Where(id => !evidenceById.ContainsKey(id)))
                    Error("unresolved-decline-evidence", $"Declined claim '{claim.Reference}' references missing evidence '{missing}'.");
                if (!correction.Any(item => item.Kind == DataConformanceEvidenceKind.Negative && item.Outcome == EvidenceOutcome.Passed))
                    Red("unproved-decline", $"Declined claim '{claim.Reference}' lacks passing corrective NEG evidence.");
                continue;
            }

            foreach (var acceptanceId in profile.AcceptanceIds)
            {
                if (!Rows.Any(row => string.Equals(row.AcceptanceId, acceptanceId, StringComparison.Ordinal) &&
                                     row.ClaimReferences.Contains(claim.Reference, StringComparer.Ordinal)))
                {
                    Error("missing-row", $"Claim '{claim.Reference}' selected '{acceptanceId}' but no row links it.");
                }
            }
        }

        foreach (var row in Rows)
        {
            DataConformanceCatalog.Cell cell;
            try { cell = DataConformanceCatalog.Acceptance(row.AcceptanceId); }
            catch (Exception exception) { Error("unknown-cell", exception.Message); continue; }
            if (!string.Equals(row.Id, RowIdentity(row.AcceptanceId, row.Case, row.Owner), StringComparison.Ordinal))
                Error("row-identity", $"Row '{row.Id}' does not match its acceptance/case/owner identity.");
            foreach (var claimReference in row.ClaimReferences.Where(reference => !claimByReference.ContainsKey(reference)))
                Error("unresolved-claim", $"Row '{row.Id}' references missing claim '{claimReference}'.");
            foreach (var evidenceReference in row.EvidenceReferences.Where(reference => !evidenceById.ContainsKey(reference)))
                Error("unresolved-evidence", $"Row '{row.Id}' references missing evidence '{evidenceReference}'.");
            foreach (var linkedRow in row.LinkedRows.Where(reference => !rowById.ContainsKey(reference)))
                Error("unresolved-linked-row", $"Row '{row.Id}' references missing linked row '{linkedRow}'.");
            if (!row.RequiredEvidence.SequenceEqual(cell.Evidence))
                Error("evidence-contract", $"Row '{row.Id}' changed the catalog's required evidence.");

            var attached = row.EvidenceReferences.Where(evidenceById.ContainsKey).Select(id => evidenceById[id]).ToArray();
            var computed = ComputeVerdict(cell.Evidence, attached, row.Blocker);
            if (row.Verdict != computed) Error("verdict", $"Row '{row.Id}' verdict is '{row.Verdict}', expected '{computed}'.");
            if (computed == RowVerdict.Red) hasRed = true;
            if (computed == RowVerdict.Deferred) hasDeferred = true;
            if (attached.Any(item => item.Outcome == EvidenceOutcome.Infrastructure)) hasInfrastructure = true;
        }

        foreach (var claim in Claims.Where(claim =>
                     claim.Scope == DataConformanceManifest.ClaimScope.Observed &&
                     claim.Publication == DataConformanceManifest.ClaimPublication.Advertised))
        {
            var claimRows = Rows.Where(row => row.ClaimReferences.Contains(claim.Reference, StringComparer.Ordinal)).ToArray();
            if (claimRows.Length == 0 || claimRows.Any(row => row.Verdict != RowVerdict.Pass))
            {
                falseClaim = true;
                Red("false-advertised-claim", $"Advertised claim '{claim.Reference}' is not fully proved.");
            }
        }

        foreach (var evidence in EvidenceItems)
        {
            try { DataConformanceCatalog.Acceptance(RowAcceptanceId(evidence.RowId)); }
            catch { Error("evidence-row", $"Evidence '{evidence.Id}' targets invalid row '{evidence.RowId}'."); }
            try { SafeArtifact(evidence.Artifact); }
            catch (Exception exception) { Error("evidence-artifact", $"Evidence '{evidence.Id}' is unsafe: {exception.Message}"); }
            if (!Enum.IsDefined(evidence.Kind) || !Enum.IsDefined(evidence.Outcome))
                Error("evidence-enum", $"Evidence '{evidence.Id}' contains an unknown kind or outcome.");
        }

        foreach (var dependency in Dependencies)
            if (!Enum.IsDefined(dependency.Kind))
                Error("dependency-enum", $"Dependency '{dependency.Id}' contains an unknown kind.");

        var status = structural ? ValidationStatus.Error
            : falseClaim || hasRed ? ValidationStatus.Red
            : hasInfrastructure ? ValidationStatus.Infrastructure
            : hasDeferred ? ValidationStatus.Deferred
            : ValidationStatus.Pass;
        return new ValidationResult(status, issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Validate just one acceptance case for the inherited executable catalog theory.</summary>
    public ValidationResult ValidateAcceptance(string acceptanceId)
    {
        var id = DataConformanceCatalog.Acceptance(acceptanceId).Id;
        var all = Validate();
        var relevantRows = Rows.Where(row => string.Equals(row.AcceptanceId, id, StringComparison.Ordinal)).ToArray();
        var relevantProfiles = DataConformanceCatalog.Profiles.Where(profile => profile.AcceptanceIds.Contains(id, StringComparer.Ordinal))
            .Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);
        var relevantClaims = Claims.Where(claim => relevantProfiles.Contains(claim.ProfileId)).Select(claim => claim.Reference)
            .ToHashSet(StringComparer.Ordinal);
        var issues = all.Issues.Where(issue =>
            issue.Message.Contains($"'{id}'", StringComparison.Ordinal) ||
            relevantRows.Any(row => issue.Message.Contains($"'{row.Id}'", StringComparison.Ordinal)) ||
            relevantClaims.Any(claim => issue.Message.Contains($"'{claim}'", StringComparison.Ordinal)) ||
            issue.Code is "schema" or "protocol" or "primer-fingerprint" or "catalog-fingerprint" or "identity")
            .ToArray();
        var hasPositive = relevantRows.Length != 0;
        var declined = Claims.Where(claim => claim.Scope == DataConformanceManifest.ClaimScope.Declined &&
                                             relevantProfiles.Contains(claim.ProfileId)).ToArray();
        if (!hasPositive && declined.Length == 0)
            issues = [.. issues, new ValidationIssue("unselected-cell", $"Acceptance cell '{id}' has no positive or declined claim.")];

        var status = issues.Any(issue => issue.Code is "schema" or "protocol" or "primer-fingerprint" or
                                           "catalog-fingerprint" or "identity" or "missing-row" or "unknown-cell" or
                                           "unresolved-evidence" or "evidence-contract" or "verdict")
            ? ValidationStatus.Error
            : relevantRows.Any(row => row.Verdict == RowVerdict.Red) ||
              issues.Any(issue => issue.Code is "unproved-decline" or "false-advertised-claim" or "unselected-cell")
                ? ValidationStatus.Red
                : relevantRows.SelectMany(row => row.EvidenceReferences).Select(id => EvidenceItems.FirstOrDefault(item => item.Id == id))
                    .Any(item => item?.Outcome == EvidenceOutcome.Infrastructure)
                    ? ValidationStatus.Infrastructure
                    : relevantRows.Any(row => row.Verdict == RowVerdict.Deferred)
                        ? ValidationStatus.Deferred
                        : ValidationStatus.Pass;
        return new ValidationResult(status, issues);
    }

    /// <summary>True when a changed dependency with the same identity no longer matches this packet's fingerprint.</summary>
    public bool IsImpactedBy(DependencyChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return Dependencies.Any(dependency => dependency.Kind == change.Kind &&
            string.Equals(dependency.Id, change.Id, StringComparison.Ordinal) &&
            !string.Equals(dependency.Fingerprint, change.Fingerprint, StringComparison.Ordinal));
    }

    /// <summary>Return every packet invalidated by any owner/path/tool/profile/fixture change.</summary>
    public static IReadOnlyList<DataConformancePacket> Impacted(
        IEnumerable<DataConformancePacket> packets,
        IEnumerable<DependencyChange> changes)
    {
        ArgumentNullException.ThrowIfNull(packets);
        ArgumentNullException.ThrowIfNull(changes);
        var changeSet = changes.ToArray();
        return packets.Where(packet => changeSet.Any(packet.IsImpactedBy))
            .OrderBy(packet => packet.Adapter, StringComparer.Ordinal).ToArray();
    }

    public static string RowIdentity(string acceptanceId, string @case, string owner) =>
        $"{RequireText(acceptanceId, "acceptance ID")}/{Slug(@case, "case")}/{Slug(owner, "owner")}";

    private static Row CompileRow(RowPlan plan, IReadOnlyList<Evidence> evidence)
    {
        var cell = DataConformanceCatalog.Acceptance(plan.AcceptanceId);
        var id = RowIdentity(plan.AcceptanceId, plan.Case, plan.Owner);
        var attached = evidence.Where(item => string.Equals(item.RowId, id, StringComparison.Ordinal))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        return new Row(
            id,
            cell.Id,
            RequireText(plan.Case, "case"),
            RequireText(plan.Owner, "owner"),
            plan.ClaimReferences.Select(reference => RequireText(reference, "claim reference")).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray(),
            (plan.LinkedRows ?? []).Select(reference => RequireText(reference, "linked row")).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray(),
            cell.Evidence,
            attached.Select(item => item.Id).ToArray(),
            ComputeVerdict(cell.Evidence, attached, plan.Blocker),
            NormalizeOptional(plan.Blocker));
    }

    private static RowVerdict ComputeVerdict(
        IReadOnlyList<DataConformanceEvidenceKind> required,
        IReadOnlyList<Evidence> evidence,
        string? blocker)
    {
        var outcomes = required.Select(kind => evidence.Where(item => item.Kind == kind).Select(item => item.Outcome).ToArray())
            .ToArray();
        if (outcomes.Any(values => values.Contains(EvidenceOutcome.Failed))) return RowVerdict.Red;
        if (outcomes.All(values => values.Contains(EvidenceOutcome.Passed))) return RowVerdict.Pass;
        if (outcomes.Any(values => values.Contains(EvidenceOutcome.Infrastructure) || values.Contains(EvidenceOutcome.Deferred)) ||
            !string.IsNullOrWhiteSpace(blocker)) return RowVerdict.Deferred;
        return RowVerdict.Red;
    }

    private static RowPlan Normalize(RowPlan value) => new(
        DataConformanceCatalog.Acceptance(value.AcceptanceId).Id,
        RequireText(value.Case, "row case"),
        RequireText(value.Owner, "row owner"),
        value.ClaimReferences.Select(reference => RequireText(reference, "claim reference")).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray(),
        (value.LinkedRows ?? []).Select(reference => RequireText(reference, "linked row")).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray(),
        NormalizeOptional(value.Blocker));

    private static Evidence Normalize(Evidence value) => new(
        RequireText(value.Id, "evidence ID"),
        RequireText(value.RowId, "evidence row ID"),
        value.Kind,
        value.Outcome,
        RequireText(value.Command, "evidence command"),
        SafeArtifact(value.Artifact));

    private static Dependency Normalize(Dependency value) => new(
        value.Kind,
        RequireText(value.Id, "dependency ID"),
        RequireText(value.Fingerprint, "dependency fingerprint"));

    private static void ValidateIdentity(Identity source)
    {
        var fingerprint = RequireText(source.SourceFingerprint, "source fingerprint");
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("Data conformance source fingerprint must be a SHA-256 value.", nameof(source));
        RequireText(source.Provider, "provider identity");
        RequireText(source.Driver, "driver identity");
        RequireText(source.Fixture, "fixture identity");
    }

    private static Dictionary<string, T> IndexUnique<T>(
        IEnumerable<T> values,
        Func<T, string> key,
        string kind,
        Action<string, string> error)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var id = key(value);
            if (!result.TryAdd(id, value)) error($"duplicate-{kind}", $"Duplicate {kind} '{id}'.");
        }
        return result;
    }

    private static void EnsureUnique(IEnumerable<string> values, string kind)
    {
        var duplicate = values.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null) throw new InvalidOperationException($"Duplicate Data conformance {kind} '{duplicate.Key}'.");
    }

    private static string? MergeBlocker(IEnumerable<string?> blockers)
    {
        var values = blockers.Select(NormalizeOptional).Where(value => value is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length > 1) throw new InvalidOperationException("Merged Data conformance row plans carry different blockers.");
        return values.SingleOrDefault();
    }

    private static string RowAcceptanceId(string rowId)
    {
        var separator = rowId.IndexOf('/', StringComparison.Ordinal);
        return separator > 0 ? rowId[..separator] : rowId;
    }

    private static string Slug(string value, string field)
    {
        var text = RequireText(value, field).ToLowerInvariant();
        var result = new char[text.Length];
        var length = 0;
        var separator = false;
        foreach (var character in text)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (separator && length != 0) result[length++] = '-';
                result[length++] = character;
                separator = false;
            }
            else separator = true;
        }
        return length == 0 ? throw new ArgumentException($"Data conformance {field} produces no stable identity.", field) : new string(result, 0, length);
    }

    private static string RequireText(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Data conformance {field} is required.", field)
            : value.Trim();

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SafeArtifact(string value)
    {
        var artifact = RequireText(value, "evidence artifact").Replace('\\', '/');
        if (artifact.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(artifact) ||
            Uri.TryCreate(artifact, UriKind.Absolute, out _) ||
            artifact.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            throw new ArgumentException(
                "Data conformance evidence artifact must be a safe repository-relative reference.", nameof(value));
        }
        return artifact;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
