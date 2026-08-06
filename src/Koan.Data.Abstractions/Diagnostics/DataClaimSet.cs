using System.Text;
using Koan.Core.Capabilities;

namespace Koan.Data.Abstractions;

/// <summary>Immutable claim and capability declaration for one adapter.</summary>
public sealed class DataClaimSet
{
    private DataClaimSet(string provider, IReadOnlyList<DataClaim> claims, IReadOnlyList<string> capabilities)
    {
        Provider = provider;
        Claims = claims;
        Capabilities = capabilities;
    }

    public string Provider { get; }
    public IReadOnlyList<DataClaim> Claims { get; }
    public IReadOnlyList<string> Capabilities { get; }

    public static DataClaimSet Describe(IAdapterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return DescribeCore(factory, descriptor: null);
    }

    public static DataClaimSet Describe(IAdapterFactory factory, string source)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var descriptor = factory is IDataSourceIntegrationFactory sourceFactory
            ? sourceFactory.DescribeSource(source.Trim()) ?? DataSourceIntegrationDescriptor.Empty
            : null;
        return DescribeCore(factory, descriptor);
    }

    public static DataClaimSet Describe(IAdapterFactory factory, DataSourceIntegrationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return DescribeCore(factory, descriptor);
    }

    private static DataClaimSet DescribeCore(IAdapterFactory factory, DataSourceIntegrationDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var builder = new Builder(factory.Provider);
        builder.Framework(DataClaimProfiles.SourceCore);
        factory.DescribeClaims(builder);
        if (descriptor is not null) builder.Source(descriptor);
        return builder.Build();
    }

    public static DataClaimSet For(string provider, Action<IDataClaims> declare)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(declare);
        var builder = new Builder(provider);
        builder.Framework(DataClaimProfiles.SourceCore);
        declare(builder);
        return builder.Build();
    }

    private sealed class Builder : IDataClaims
    {
        private readonly string _provider;
        private readonly List<DataClaim> _claims = [];
        private readonly HashSet<string> _capabilities = new(StringComparer.Ordinal);
        private readonly HashSet<(string Profile, string? Qualifier)> _keys = new(ProfileKeyComparer.Instance);
        private bool _built;

        public Builder(string provider)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(provider);
            _provider = provider.Trim();
        }

        public IDataClaims Profile(string profile, string? qualifier = null, bool advertised = true)
        {
            Add(profile, "Adapter", qualifier, capability: null, advertised);
            return this;
        }

        public IDataClaims Capability(Capability capability, bool advertised = true)
        {
            EnsureMutable();
            _capabilities.Add(capability.Id);
            if (DataCapabilityProfiles.TryGet(capability, out var profile))
                Add(profile, "Adapter", capability.Id, capability.Id, advertised);
            return this;
        }

        internal void Framework(string profile) => Add(profile, "Framework", qualifier: null, capability: null, advertised: false);

        internal void Source(DataSourceIntegrationDescriptor descriptor)
        {
            var operations = descriptor.Operations;
            var inspection = descriptor.Inspection;

            ProfileWhen(
                operations.HasFlag(SourceIntegrationCapabilities.RegisteredRecords) ||
                operations.HasFlag(SourceIntegrationCapabilities.RegisteredScalar),
                DataClaimProfiles.RegisteredReads);
            ProfileWhen(
                operations.HasFlag(SourceIntegrationCapabilities.RegisteredRecords) ||
                inspection.HasFlag(SourceInspectionCapabilities.SampleRecords),
                DataClaimProfiles.RecordResults);
            ProfileWhen(
                inspection.HasFlag(SourceInspectionCapabilities.ListContainers),
                DataClaimProfiles.ContainerListing);
            ProfileWhen(
                inspection.HasFlag(SourceInspectionCapabilities.ResolveAddress),
                DataClaimProfiles.ContainerAddressResolution);
            ProfileWhen(
                inspection.HasFlag(SourceInspectionCapabilities.DescribeContainer),
                DataClaimProfiles.ContainerDescription);
            ProfileWhen(
                inspection.HasFlag(SourceInspectionCapabilities.SampleRecords),
                DataClaimProfiles.RecordSampling);
        }

        internal DataClaimSet Build()
        {
            EnsureMutable();
            _built = true;
            return new DataClaimSet(
                _provider,
                _claims.OrderBy(static claim => claim.Reference, StringComparer.Ordinal).ToArray(),
                _capabilities.Order(StringComparer.Ordinal).ToArray());
        }

        private void Add(string profile, string owner, string? qualifier, string? capability, bool advertised)
        {
            EnsureMutable();
            var normalizedProfile = Require(profile, "profile");
            var normalizedQualifier = string.IsNullOrWhiteSpace(qualifier) ? null : qualifier.Trim();
            if (!_keys.Add((normalizedProfile, normalizedQualifier)))
                throw new InvalidOperationException(
                    $"Data claim '{normalizedProfile}'{(normalizedQualifier is null ? string.Empty : $" ({normalizedQualifier})")} is already declared for '{_provider}'.");
            _claims.Add(new DataClaim(
                $"CLM-{Slug(_provider)}-{Slug(normalizedProfile)}-observed{(normalizedQualifier is null ? string.Empty : $"-{Slug(normalizedQualifier)}")}",
                normalizedProfile,
                owner,
                normalizedQualifier,
                capability,
                advertised));
        }

        private void ProfileWhen(bool condition, string profile)
        {
            if (condition && !_keys.Contains((profile, null)))
                Add(profile, "Adapter", qualifier: null, capability: null, advertised: true);
        }

        private void EnsureMutable()
        {
            if (_built) throw new InvalidOperationException("The Data claim set is already frozen.");
        }
    }

    private sealed class ProfileKeyComparer : IEqualityComparer<(string Profile, string? Qualifier)>
    {
        public static ProfileKeyComparer Instance { get; } = new();

        public bool Equals((string Profile, string? Qualifier) x, (string Profile, string? Qualifier) y) =>
            StringComparer.Ordinal.Equals(x.Profile, y.Profile) &&
            StringComparer.Ordinal.Equals(x.Qualifier, y.Qualifier);

        public int GetHashCode((string Profile, string? Qualifier) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Profile),
                value.Qualifier is null ? 0 : StringComparer.Ordinal.GetHashCode(value.Qualifier));
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"Data claim {field} is required.", field) : value.Trim();

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
        return result.Length == 0 ? throw new InvalidOperationException("A Data claim identifier produced no slug.") : result.ToString();
    }
}
