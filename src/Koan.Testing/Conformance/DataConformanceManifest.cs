using System.Text;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Testing.Conformance.Infrastructure;

namespace Koan.Testing;

/// <summary>
/// One immutable declaration of an adapter's Observed, Target, and Declined Data profiles. Existing runtime capability
/// tokens project through <see cref="Builder.From(CapabilitySet)"/>; profiles without a positive claim become explicit, unproved
/// declines so omission can never read as support.
/// </summary>
public sealed class DataConformanceManifest
{
    private static readonly IReadOnlyDictionary<Capability, string> VectorCapabilityProfileMap =
        new Dictionary<Capability, string>
        {
            [new(DataConformanceConstants.VectorKnnCapability)] = DataConformanceProfiles.VectorCore,
            [new(DataConformanceConstants.VectorFiltersCapability)] = DataConformanceProfiles.VectorFilters,
            [new(DataConformanceConstants.VectorHybridCapability)] = DataConformanceProfiles.VectorHybrid,
            [new(DataConformanceConstants.VectorNativeContinuationCapability)] = DataConformanceProfiles.VectorContinuation,
            [new(DataConformanceConstants.VectorMultiVectorPerEntityCapability)] = DataConformanceProfiles.NamedVectorSpaces,
            [new(DataConformanceConstants.VectorBulkUpsertCapability)] = DataConformanceProfiles.VectorBulk,
            [new(DataConformanceConstants.VectorBulkDeleteCapability)] = DataConformanceProfiles.VectorBulk,
            [new(DataConformanceConstants.VectorAtomicBatchCapability)] = DataConformanceProfiles.VectorAtomicBatch,
            [new(DataConformanceConstants.VectorScoreNormalizationCapability)] = DataConformanceProfiles.VectorCore,
            [new(DataConformanceConstants.VectorDynamicCollectionsCapability)] = DataConformanceProfiles.ManagedVectorLifecycle,
        };

    private static readonly IReadOnlySet<Capability> RetiredVectorCapabilitySet = new HashSet<Capability>
    {
        new(DataConformanceConstants.VectorStreamingResultsCapability),
    };

    /// <summary>The evaluation scope defined by the primer.</summary>
    public enum ClaimScope
    {
        Observed,
        Target,
        Declined,
    }

    /// <summary>Whether the pinned public surface announces a claim.</summary>
    public enum ClaimPublication
    {
        Advertised,
        Unadvertised,
    }

    /// <summary>One profile claim from which acceptance cells are selected.</summary>
    public sealed record Claim(
        string Reference,
        string ProfileId,
        ClaimScope Scope,
        ClaimPublication Publication,
        string Owner,
        string? Qualifier,
        IReadOnlyList<string> CorrectiveEvidenceIds);

    /// <summary>Fluent claim builder used only during manifest construction.</summary>
    public sealed class Builder
    {
        private readonly string _adapter;
        private readonly List<Claim> _claims = [];
        private bool _built;

        internal Builder(string adapter)
        {
            _adapter = adapter;
            Add(DataConformanceProfiles.SourceCore, ClaimScope.Observed, ClaimPublication.Unadvertised,
                DataConformanceConstants.FrameworkOwner, qualifier: null, []);
        }

        /// <summary>Declare an observed profile in the pinned adapter.</summary>
        public Builder Observe(
            string profile,
            bool advertised = false,
            string owner = DataConformanceConstants.AdapterOwner,
            string? qualifier = null) =>
            Add(profile, ClaimScope.Observed,
                advertised ? ClaimPublication.Advertised : ClaimPublication.Unadvertised,
                owner, qualifier, []);

        /// <summary>Declare a human-approved, unadvertised target profile.</summary>
        public Builder Target(
            string profile,
            string owner = DataConformanceConstants.AdapterOwner,
            string? qualifier = null) =>
            Add(profile, ClaimScope.Target, ClaimPublication.Unadvertised, owner, qualifier, []);

        /// <summary>Decline an optional profile and name the negative evidence that proves corrective failure.</summary>
        public Builder Decline(string profile, params string[] correctiveEvidenceIds) =>
            Add(profile, ClaimScope.Declined, ClaimPublication.Unadvertised,
                DataConformanceConstants.AdapterOwner, qualifier: null, correctiveEvidenceIds);

        /// <summary>
        /// Project existing runtime capability tokens into their corresponding primer profiles. Tokens without a
        /// profile in the current base primer remain runtime facts and do not invent conformance semantics.
        /// </summary>
        public Builder From(CapabilitySet capabilities)
        {
            ArgumentNullException.ThrowIfNull(capabilities);
            foreach (var (token, profile) in DataCapabilityProfiles.All.OrderBy(item => item.Key.Id, StringComparer.Ordinal))
                AddWhenPresent(capabilities, token, profile);
            return this;
        }

        /// <summary>Consumes the exact executable claim references published by the adapter at runtime.</summary>
        public Builder From(DataClaimSet claims)
        {
            ArgumentNullException.ThrowIfNull(claims);
            if (!string.Equals(_adapter, claims.Provider, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Data claim provider '{claims.Provider}' does not match manifest adapter '{_adapter}'.");
            foreach (var claim in claims.Claims.OrderBy(static claim => claim.Reference, StringComparer.Ordinal))
            {
                if (string.Equals(claim.Profile, DataConformanceProfiles.SourceCore, StringComparison.Ordinal) &&
                    claim.Qualifier is null) continue;
                Add(claim.Profile, ClaimScope.Observed,
                    claim.Advertised ? ClaimPublication.Advertised : ClaimPublication.Unadvertised,
                    claim.Owner, claim.Qualifier, []);
            }
            return this;
        }

        /// <summary>
        /// Project ratified Vector capability tokens into primer profiles. A legacy token whose meaning contradicts
        /// the ratified regular result contract rejects with a correction instead of silently inventing a profile.
        /// </summary>
        public Builder FromVector(CapabilitySet capabilities)
        {
            ArgumentNullException.ThrowIfNull(capabilities);
            var incompatible = capabilities.All.Where(RetiredVectorCapabilitySet.Contains)
                .OrderBy(token => token.Id, StringComparer.Ordinal).ToArray();
            if (incompatible.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Vector capabilities contradict the ratified buffered-result contract: " +
                    $"{string.Join(", ", incompatible.Select(token => token.Id))}. " +
                    "Withdraw the legacy claim; provider-bounded export is declared through the Vector Export profile.");
            }

            foreach (var (token, profile) in VectorCapabilityProfileMap.OrderBy(item => item.Key.Id, StringComparer.Ordinal))
                AddWhenPresent(capabilities, token, profile);
            return this;
        }

        internal DataConformanceManifest Build()
        {
            EnsureMutable();
            _built = true;
            var claimedProfiles = _claims.Select(claim => claim.ProfileId).ToHashSet(StringComparer.Ordinal);
            foreach (var profile in DataConformanceCatalog.Profiles)
            {
                if (claimedProfiles.Contains(profile.Id)) continue;
                _claims.Add(NewClaim(
                    profile.Id,
                    ClaimScope.Declined,
                    ClaimPublication.Unadvertised,
                    DataConformanceConstants.AdapterOwner,
                    qualifier: null,
                    correctiveEvidenceIds: []));
            }

            var ordered = _claims.OrderBy(claim => claim.Reference, StringComparer.Ordinal).ToArray();
            var duplicateReferences = ordered.GroupBy(claim => claim.Reference, StringComparer.Ordinal)
                .Where(group => group.Count() != 1).Select(group => group.Key).ToArray();
            if (duplicateReferences.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Data conformance claim references are duplicated: {string.Join(", ", duplicateReferences)}.");
            }
            return new DataConformanceManifest(_adapter, ordered);
        }

        private Builder Add(
            string profile,
            ClaimScope scope,
            ClaimPublication publication,
            string owner,
            string? qualifier,
            IEnumerable<string> correctiveEvidenceIds)
        {
            EnsureMutable();
            var profileId = DataConformanceCatalog.ResolveProfile(profile).Id;
            var normalizedQualifier = NormalizeOptional(qualifier);
            var evidence = correctiveEvidenceIds.Select(id => RequireText(id, "corrective evidence ID"))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (scope != ClaimScope.Declined && evidence.Length != 0)
            {
                throw new InvalidOperationException("Only a Declined Data conformance claim carries corrective evidence.");
            }
            if (scope == ClaimScope.Target && publication != ClaimPublication.Unadvertised)
            {
                throw new InvalidOperationException("A Target Data conformance claim must remain unadvertised.");
            }

            var conflicts = _claims.Where(claim =>
                    string.Equals(claim.ProfileId, profileId, StringComparison.Ordinal) &&
                    string.Equals(claim.Qualifier, normalizedQualifier, StringComparison.Ordinal))
                .ToArray();
            if (conflicts.Any(claim => claim.Scope == scope) ||
                (scope == ClaimScope.Declined && conflicts.Length != 0) ||
                (scope != ClaimScope.Declined && conflicts.Any(claim => claim.Scope == ClaimScope.Declined)))
            {
                throw new InvalidOperationException(
                    $"Data conformance profile '{profileId}'{QualifierText(normalizedQualifier)} has a conflicting {scope} declaration.");
            }

            _claims.Add(NewClaim(profileId, scope, publication, RequireText(owner, "claim owner"),
                normalizedQualifier, evidence));
            return this;
        }

        private void AddWhenPresent(CapabilitySet capabilities, Capability token, string profile)
        {
            if (capabilities.Has(token)) Observe(profile, qualifier: token.Id);
        }

        private Claim NewClaim(
            string profile,
            ClaimScope scope,
            ClaimPublication publication,
            string owner,
            string? qualifier,
            IReadOnlyList<string> correctiveEvidenceIds)
        {
            var reference = $"CLM-{Slug(_adapter)}-{Slug(profile)}-{scope.ToString().ToLowerInvariant()}";
            if (qualifier is not null) reference += $"-{Slug(qualifier)}";
            return new Claim(reference, profile, scope, publication, owner, qualifier, correctiveEvidenceIds);
        }

        private void EnsureMutable()
        {
            if (_built) throw new InvalidOperationException("The Data conformance manifest builder is already frozen.");
        }
    }

    private DataConformanceManifest(string adapter, IReadOnlyList<Claim> claims)
    {
        Adapter = adapter;
        Claims = claims;
    }

    /// <summary>The stable adapter identity used in packet rows and generated claim inputs.</summary>
    public string Adapter { get; }

    /// <summary>The complete claim set, including explicit unproved declines for omitted optional profiles.</summary>
    public IReadOnlyList<Claim> Claims { get; }

    /// <summary>The complete built-in Data capability-to-profile registry used by <see cref="Builder.From(CapabilitySet)"/>.</summary>
    public static IReadOnlyDictionary<Capability, string> CapabilityProfiles => DataCapabilityProfiles.All;

    /// <summary>The ratified Vector capability-to-profile projection used by <see cref="Builder.FromVector"/>.</summary>
    public static IReadOnlyDictionary<Capability, string> VectorCapabilityProfiles => VectorCapabilityProfileMap;

    /// <summary>Legacy Vector tokens that conflict with the ratified contract and cannot select a profile.</summary>
    public static IReadOnlySet<Capability> RetiredVectorCapabilities => RetiredVectorCapabilitySet;

    /// <summary>Construct and freeze one adapter manifest.</summary>
    public static DataConformanceManifest For(string adapter, Action<Builder> declare)
    {
        var id = RequireText(adapter, "adapter ID");
        ArgumentNullException.ThrowIfNull(declare);
        var builder = new Builder(id);
        declare(builder);
        return builder.Build();
    }

    private static string Slug(string value)
    {
        var result = new StringBuilder(value.Length);
        var separator = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (separator && result.Length != 0) result.Append('-');
                result.Append(character);
                separator = false;
            }
            else separator = true;
        }
        return result.Length == 0 ? throw new InvalidOperationException("A Data conformance identifier produced no slug.") : result.ToString();
    }

    private static string RequireText(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Data conformance {field} is required.", field)
            : value.Trim();

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string QualifierText(string? qualifier) => qualifier is null ? string.Empty : $" ({qualifier})";
}
