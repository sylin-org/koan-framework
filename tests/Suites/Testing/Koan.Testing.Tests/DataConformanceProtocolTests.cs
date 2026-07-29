using System.Reflection;
using System.Text.Json.Nodes;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Testing;
using Xunit;

namespace Koan.Testing.Tests;

public sealed class DataConformanceProtocolTests
{
    [Fact]
    public void Generated_catalog_registers_every_stable_cell_and_profile()
    {
        Assert.Equal(105, DataConformanceCatalog.Cells.Count);
        Assert.Equal(39, DataConformanceCatalog.Profiles.Count);
        Assert.Equal("A-01", DataConformanceCatalog.Cells[0].Id);
        Assert.Equal("V-24", DataConformanceCatalog.Cells[^1].Id);
        Assert.Equal(105, DataConformanceCatalog.Cells.Select(cell => cell.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(DataConformanceCatalog.Cells, cell =>
        {
            Assert.NotEmpty(cell.Evidence);
            Assert.Equal(
                "Koan.Testing.DataAdapterConformanceSpecs.Acceptance_cell_has_complete_evidence",
                cell.Verifier);
        });
        Assert.Equal(105, DataAdapterConformanceSpecs.AcceptanceIds().Count());
    }

    [Fact]
    public void Every_built_in_data_capability_maps_to_objective_cells()
    {
        var tokens = typeof(DataCaps).GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(Capability))
            .Select(field => (Capability)field.GetValue(null)!)
            .OrderBy(token => token.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(tokens, DataConformanceManifest.CapabilityProfiles.Keys.OrderBy(token => token.Id, StringComparer.Ordinal));
        Assert.All(DataConformanceManifest.CapabilityProfiles.Values, profile =>
            Assert.NotEmpty(DataConformanceCatalog.ResolveProfile(profile).AcceptanceIds));
    }

    [Fact]
    public void Every_built_in_vector_capability_is_projected_or_explicitly_retired()
    {
        var tokens = typeof(VectorCaps).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Capability))
            .Select(field => (Capability)field.GetValue(null)!)
            .OrderBy(token => token.Id, StringComparer.Ordinal)
            .ToArray();
        var classified = DataConformanceManifest.VectorCapabilityProfiles.Keys
            .Concat(DataConformanceManifest.RetiredVectorCapabilities)
            .OrderBy(token => token.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(tokens, classified);
        Assert.Empty(DataConformanceManifest.VectorCapabilityProfiles.Keys
            .Intersect(DataConformanceManifest.RetiredVectorCapabilities));
        Assert.All(DataConformanceManifest.VectorCapabilityProfiles.Values, profile =>
            Assert.NotEmpty(DataConformanceCatalog.ResolveProfile(profile).AcceptanceIds));
    }

    [Fact]
    public void Vector_capability_projection_is_deterministic_and_incompatible_legacy_claims_fail_closed()
    {
        var capabilities = CapabilitySet.Build("vector.sample", caps => caps
            .Add(VectorCaps.Knn)
            .Add(VectorCaps.Filters)
            .Add(VectorCaps.BulkUpsert));

        var manifest = DataConformanceManifest.For("vector-sample", claims => claims.FromVector(capabilities));
        Assert.Contains(manifest.Claims, claim => claim.ProfileId == DataConformanceProfiles.VectorCore &&
                                                  claim.Qualifier == VectorCaps.Knn.Id);
        Assert.Contains(manifest.Claims, claim => claim.ProfileId == DataConformanceProfiles.VectorFilters &&
                                                  claim.Qualifier == VectorCaps.Filters.Id);
        Assert.Contains(manifest.Claims, claim => claim.ProfileId == DataConformanceProfiles.VectorBulk &&
                                                  claim.Qualifier == VectorCaps.BulkUpsert.Id);

        var incompatible = CapabilitySet.Build("vector.legacy", caps => caps.Add(VectorCaps.StreamingResults));
        var error = Assert.Throws<InvalidOperationException>(() =>
            DataConformanceManifest.For("vector-legacy", claims => claims.FromVector(incompatible)));
        Assert.Contains(VectorCaps.StreamingResults.Id, error.Message, StringComparison.Ordinal);
        Assert.Contains("Withdraw", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capability_projection_and_implicit_declines_are_complete_and_deterministic()
    {
        var capabilities = CapabilitySet.Build("sample", caps => caps
            .Add(DataCaps.Query.Filter)
            .Add(DataCaps.Write.AtomicBatch)
            .Add(DataCaps.Isolation.RowScoped));

        var first = DataConformanceManifest.For("sample", claims => claims.From(capabilities));
        var second = DataConformanceManifest.For("sample", claims => claims.From(capabilities));

        Assert.Equal(first.Claims, second.Claims);
        Assert.Contains(first.Claims, claim => claim.ProfileId == DataConformanceProfiles.SourceCore &&
                                               claim.Scope == DataConformanceManifest.ClaimScope.Observed);
        Assert.Contains(first.Claims, claim => claim.ProfileId == DataConformanceProfiles.AtomicBatch &&
                                               claim.Qualifier == DataCaps.Write.AtomicBatch.Id);
        Assert.Contains(first.Claims, claim => claim.ProfileId == DataConformanceProfiles.Isolation &&
                                               claim.Qualifier == DataCaps.Isolation.RowScoped.Id);
        Assert.Equal(39, first.Claims.Select(claim => claim.ProfileId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Runtime_claims_and_TestKit_share_the_exact_profile_projection_and_references()
    {
        var runtime = DataClaimSet.For("sample", claims => claims
            .Capability(DataCaps.Query.ProviderBoundedPaging)
            .Capability(DataCaps.Write.AtomicBatch)
            .Profile(DataClaimProfiles.RegisteredReads));

        var manifest = DataConformanceManifest.For("sample", claims => claims.From(runtime));
        var observed = manifest.Claims
            .Where(claim => claim.Scope == DataConformanceManifest.ClaimScope.Observed)
            .OrderBy(claim => claim.Reference, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(runtime.Claims.Select(claim => claim.Reference).Order(StringComparer.Ordinal),
            observed.Select(claim => claim.Reference));
        Assert.Contains(observed, claim => claim.ProfileId == DataClaimProfiles.ProviderBoundedPaging &&
                                           claim.Qualifier == DataCaps.Query.ProviderBoundedPaging.Id &&
                                           claim.Publication == DataConformanceManifest.ClaimPublication.Advertised);
        Assert.Same(DataCapabilityProfiles.All, DataConformanceManifest.CapabilityProfiles);
    }

    [Fact]
    public void Packet_is_byte_stable_and_mechanically_green_when_every_proof_passes()
    {
        var first = BuildCompletePacket();
        var second = BuildCompletePacket();

        Assert.Equal(first.ToJson(), second.ToJson());
        var result = first.Validate();
        Assert.Equal(DataConformancePacket.ValidationStatus.Pass, result.Status);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("unknown-cell", "unknown-cell")]
    [InlineData("duplicate-row", "duplicate-row")]
    [InlineData("unresolved-evidence", "unresolved-evidence")]
    [InlineData("stale-catalog", "catalog-fingerprint")]
    [InlineData("unsafe-artifact", "evidence-artifact")]
    public void Structural_packet_mutations_fail_closed(string mutation, string expectedCode)
    {
        var node = JsonNode.Parse(BuildCompletePacket().ToJson())!.AsObject();
        var rows = node["rows"]!.AsArray();
        switch (mutation)
        {
            case "unknown-cell":
                rows[0]!["acceptanceId"] = "A-99";
                break;
            case "duplicate-row":
                rows.Add(rows[0]!.DeepClone());
                break;
            case "unresolved-evidence":
                rows[0]!["evidenceReferences"]!.AsArray().Add("EV-MISSING");
                break;
            case "stale-catalog":
                node["catalogSha256"] = new string('0', 64);
                break;
            case "unsafe-artifact":
                node["evidenceItems"]!.AsArray()[0]!["artifact"] = "../outside/result.json";
                break;
            default:
                throw new InvalidOperationException($"Unknown mutation '{mutation}'.");
        }

        var result = DataConformancePacket.FromJson(node.ToJsonString()).Validate();
        Assert.Equal(DataConformancePacket.ValidationStatus.Error, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void Advertised_claim_without_complete_evidence_is_red()
    {
        var manifest = DataConformanceManifest.For("sample", claims =>
        {
            claims.Observe(DataConformanceProfiles.EntityPersistence, advertised: true);
            foreach (var profile in DataConformanceCatalog.Profiles.Where(profile =>
                         profile.Id is not DataConformanceProfiles.SourceCore and not DataConformanceProfiles.EntityPersistence))
                claims.Decline(profile.Id);
        });
        var packet = DataConformancePacket.Compile(manifest, TestIdentity());

        var result = packet.Validate();
        Assert.Equal(DataConformancePacket.ValidationStatus.Red, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "false-advertised-claim");
    }

    [Fact]
    public void Advertised_vector_filter_claim_without_complete_evidence_is_red()
    {
        var manifest = DataConformanceManifest.For("vector-sample", claims =>
        {
            claims.Observe(DataConformanceProfiles.VectorCore);
            claims.Observe(DataConformanceProfiles.VectorFilters, advertised: true);
            foreach (var profile in DataConformanceCatalog.Profiles.Where(profile =>
                         profile.Id is not DataConformanceProfiles.SourceCore and
                         not DataConformanceProfiles.VectorCore and
                         not DataConformanceProfiles.VectorFilters))
                claims.Decline(profile.Id);
        });

        var result = DataConformancePacket.Compile(manifest, TestIdentity()).Validate();
        Assert.Equal(DataConformancePacket.ValidationStatus.Red, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "false-advertised-claim" &&
                                                issue.Message.Contains("vector-filters", StringComparison.Ordinal));
    }

    [Fact]
    public void Skipped_live_evidence_is_infrastructure_not_green()
    {
        var packet = BuildCompletePacket(infrastructureLive: true);
        var result = packet.Validate();

        Assert.Equal(DataConformancePacket.ValidationStatus.Infrastructure, result.Status);
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(packet.Rows, row => row.Verdict == DataConformancePacket.RowVerdict.Deferred);
    }

    [Fact]
    public void Unavailable_vector_provider_evidence_is_infrastructure_not_green()
    {
        var packet = BuildCompletePacket(
            infrastructureLive: true,
            infrastructureAcceptanceId: "V-03",
            positiveProfile: DataConformanceProfiles.VectorCore);
        var result = packet.Validate();

        Assert.Equal(DataConformancePacket.ValidationStatus.Infrastructure, result.Status);
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(packet.Rows, row => row.AcceptanceId == "V-03" &&
                                            row.Verdict == DataConformancePacket.RowVerdict.Deferred);
    }

    [Fact]
    public void Impact_query_invalidates_every_matching_consumer_only()
    {
        var packet = BuildCompletePacket();
        var changed = packet.Dependencies.Select(dependency => new DataConformancePacket.DependencyChange(
            dependency.Kind,
            dependency.Id,
            dependency.Fingerprint + "-changed")).ToArray();
        var unrelated = new DataConformancePacket.DependencyChange(
            DataConformancePacket.DependencyKind.SourcePath,
            "src/Koan.Web/Other.cs",
            new string('b', 64));

        Assert.All(changed, change => Assert.True(packet.IsImpactedBy(change)));
        Assert.False(packet.IsImpactedBy(unrelated));
        Assert.Single(DataConformancePacket.Impacted([packet], [.. changed, unrelated]));
    }

    private static DataConformancePacket BuildCompletePacket(
        bool infrastructureLive = false,
        string? infrastructureAcceptanceId = null,
        string? positiveProfile = null)
    {
        var declineEvidence = new Dictionary<string, string>(StringComparer.Ordinal);
        var manifest = DataConformanceManifest.For("sample", claims =>
        {
            if (positiveProfile is not null) claims.Observe(positiveProfile);
            foreach (var profile in DataConformanceCatalog.Profiles.Where(profile =>
                         profile.Id != DataConformanceProfiles.SourceCore && profile.Id != positiveProfile))
            {
                var evidenceId = "EV-DECLINE-" + Slug(profile.Id);
                declineEvidence[profile.Id] = evidenceId;
                claims.Decline(profile.Id, evidenceId);
            }
        });
        var draft = DataConformancePacket.Compile(manifest, TestIdentity());
        var evidence = new List<DataConformancePacket.Evidence>();
        var counter = 0;
        var infrastructureAssigned = false;
        foreach (var row in draft.Rows)
        {
            foreach (var kind in row.RequiredEvidence)
            {
                var outcome = infrastructureLive && !infrastructureAssigned && kind == DataConformanceEvidenceKind.Live &&
                              (infrastructureAcceptanceId is null ||
                               string.Equals(row.AcceptanceId, infrastructureAcceptanceId, StringComparison.Ordinal))
                    ? DataConformancePacket.EvidenceOutcome.Infrastructure
                    : DataConformancePacket.EvidenceOutcome.Passed;
                if (outcome == DataConformancePacket.EvidenceOutcome.Infrastructure) infrastructureAssigned = true;
                evidence.Add(new(
                    $"EV-{counter++:D4}",
                    row.Id,
                    kind,
                    outcome,
                    $"dotnet test --filter {row.AcceptanceId}",
                    $"artifacts/{row.AcceptanceId.ToLowerInvariant()}/{kind.ToString().ToLowerInvariant()}.json"));
            }
        }

        var proofRow = draft.Rows.First(row => row.RequiredEvidence.Contains(DataConformanceEvidenceKind.Negative));
        foreach (var evidenceId in declineEvidence.Values.Order(StringComparer.Ordinal))
        {
            evidence.Add(new(
                evidenceId,
                proofRow.Id,
                DataConformanceEvidenceKind.Negative,
                DataConformancePacket.EvidenceOutcome.Passed,
                "dotnet test --filter DeclinedProfiles",
                $"artifacts/declines/{evidenceId.ToLowerInvariant()}.json"));
        }

        return DataConformancePacket.Compile(
            manifest,
            TestIdentity(),
            evidence,
            [new(
                DataConformancePacket.DependencyKind.SourcePath,
                "src/Koan.Data.Core/Policy.cs",
                new string('a', 64)),
             new(
                 DataConformancePacket.DependencyKind.Owner,
                 "Framework/Data policy",
                 new string('c', 64))]);
    }

    private static DataConformancePacket.Identity TestIdentity() => new(
        new string('a', 64),
        "provider/1",
        "driver/1",
        "fixture/1");

    private static string Slug(string value) =>
        new(value.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray());
}
