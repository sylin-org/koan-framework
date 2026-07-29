using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Options;

namespace Koan.Data.Core.SourceIntegration.Runtime;

internal sealed class DataSourceInspector(
    ResolvedSource source,
    RecordSetMaterializer materializer,
    SourceContinuationCodec continuations,
    SourceIntegrationOptions options) : IDataSourceInspector
{
    public async Task<StorageContainerPage> Containers(
        int take,
        string? continuation = null,
        CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        ct.ThrowIfCancellationRequested();
        source.Plan.Demand(DataOperationEffect.Read, "source container listing");
        var inspector = Require(SourceInspectionCapabilities.ListContainers, "list containers");
        var providerContinuation = continuation is null
            ? null
            : continuations.Decode(source.Plan, continuation);
        var batch = await inspector.Containers(take, providerContinuation, ct).ConfigureAwait(false)
            ?? throw Invalid("The provider returned no container page.");
        if (!Enum.IsDefined(batch.Completion))
            throw Invalid("The provider returned an unknown container-page completion.");
        if (batch.Containers is null || batch.Containers.Count > take)
            throw Invalid($"The provider returned more than the requested {take} containers.");
        if (batch.Completion == StorageContainerPageCompletion.MoreAvailable &&
            string.IsNullOrWhiteSpace(batch.ProviderContinuation))
            throw Invalid("MoreAvailable requires a resumable provider continuation.");
        if (batch.Completion == StorageContainerPageCompletion.Complete &&
            batch.ProviderContinuation is not null)
            throw Invalid("A complete container page cannot carry a continuation.");

        var projected = batch.Containers.Select(Project).ToArray();
        var wrapped = string.IsNullOrWhiteSpace(batch.ProviderContinuation)
            ? null
            : continuations.Encode(source.Plan, batch.ProviderContinuation);
        return new StorageContainerPage(projected, batch.Completion, wrapped);
    }

    public async Task<StorageContainerReference> Resolve(
        StorageAddress address,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ct.ThrowIfCancellationRequested();
        source.Plan.Demand(DataOperationEffect.Read, "source container resolution");
        var reference = await Require(SourceInspectionCapabilities.ResolveAddress, "resolve an address")
            .Resolve(address, ct)
            .ConfigureAwait(false);
        EnsureSource(reference);
        return reference;
    }

    public async Task<StorageContainerDescriptor> Describe(
        StorageContainerReference reference,
        CancellationToken ct = default)
    {
        EnsureSource(reference);
        ct.ThrowIfCancellationRequested();
        source.Plan.Demand(DataOperationEffect.Read, "source container description");
        var descriptor = await Require(SourceInspectionCapabilities.DescribeContainer, "describe a container")
            .Describe(reference, ct)
            .ConfigureAwait(false);
        return Project(descriptor);
    }

    public async Task<RecordSet> Sample(
        StorageContainerReference reference,
        int take,
        CancellationToken ct = default)
    {
        EnsureSource(reference);
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        ct.ThrowIfCancellationRequested();
        source.Plan.Demand(DataOperationEffect.Read, "source container sample");
        var inspector = Require(SourceInspectionCapabilities.SampleRecords, "sample records");
        var descriptor = await Describe(reference, ct).ConfigureAwait(false);
        if ((descriptor.Traits & StorageContainerTraits.Records) == 0 ||
            (descriptor.EffectiveOperations & StorageContainerOperations.Sample) == 0)
            throw Invalid($"Container '{descriptor.DisplayPath}' does not declare record sampling.");

        var maxRecords = Math.Min(take, options.MaxRecords);
        var limits = Limits(maxRecords);
        var reader = await inspector.Sample(reference, maxRecords, ct).ConfigureAwait(false);
        if (reader is null) throw Invalid("The provider returned no neutral sample reader.");
        if (descriptor.RecordShape is not null && !SameShape(descriptor.RecordShape, reader.Fields))
        {
            await reader.DisposeAsync().ConfigureAwait(false);
            throw Invalid("The sampled record shape does not match the described neutral shape.");
        }
        return await materializer.Materialize(
                reader,
                limits,
                $"sample:{source.Plan.Source}:{reference.Address}",
                ct)
            .ConfigureAwait(false);
    }

    public TNative? As<TNative>() where TNative : class, IDataSourceNativeInspector =>
        source.Integration.Inspector?.Native as TNative;

    private IDataSourceInspectorAdapter Require(SourceInspectionCapabilities capability, string operation)
    {
        var inspector = source.Integration.Inspector;
        if (inspector is null || (inspector.Capabilities & capability) != capability)
            throw Invalid($"Adapter '{source.Provider}' does not support {operation}.");
        return inspector;
    }

    private StorageContainerDescriptor Project(StorageContainerDescriptor descriptor)
    {
        if (descriptor is null) throw Invalid("The provider returned no container descriptor.");
        EnsureSource(descriptor.Reference);
        if (!SameAddress(descriptor.Reference.Address, descriptor.Address))
            throw Invalid("The provider descriptor address does not match its opaque reference.");
        if (string.IsNullOrWhiteSpace(descriptor.DisplayPath) || string.IsNullOrWhiteSpace(descriptor.ProviderKind))
            throw Invalid("A container descriptor requires a safe display path and provider kind.");
        if (descriptor.RecordShape is not null)
            for (var ordinal = 0; ordinal < descriptor.RecordShape.Count; ordinal++)
                if (descriptor.RecordShape[ordinal].Ordinal != ordinal)
                    throw Invalid("A described record shape must have contiguous ordinals in field order.");

        var operations = descriptor.EffectiveOperations;
        var capabilities = source.Integration.Inspector?.Capabilities ?? SourceInspectionCapabilities.None;
        if ((capabilities & SourceInspectionCapabilities.DescribeContainer) == 0)
            operations &= ~StorageContainerOperations.Describe;
        if ((capabilities & SourceInspectionCapabilities.SampleRecords) == 0 ||
            (descriptor.Traits & StorageContainerTraits.Records) == 0)
            operations &= ~(StorageContainerOperations.Sample | StorageContainerOperations.Query);
        if (source.Plan.Access == DataSourceAccess.ReadOnly ||
            (descriptor.Traits & StorageContainerTraits.ReadOnly) != 0)
            operations &= ~StorageContainerOperations.Write;

        return descriptor with { EffectiveOperations = operations };
    }

    private void EnsureSource(StorageContainerReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!string.Equals(reference.Source, source.Plan.Source, StringComparison.OrdinalIgnoreCase))
            throw new StorageReferenceSourceMismatchException(source.Plan.Source, reference.Source);
    }

    private RecordSetLimits Limits(int maxRecords)
    {
        var limits = new RecordSetLimits(
            maxRecords,
            options.MaxBytes,
            options.MaxValueBytes,
            options.MaxDuration);
        limits.Validate();
        return limits;
    }

    private SourceIntegrationException Invalid(string correction) =>
        new(source.Plan.Source, correction);

    private static bool SameAddress(StorageAddress left, StorageAddress right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.Namespace.SequenceEqual(right.Namespace, StringComparer.Ordinal);

    private static bool SameShape(IReadOnlyList<DataField> expected, IReadOnlyList<DataField> actual)
    {
        if (expected.Count != actual.Count) return false;
        for (var ordinal = 0; ordinal < expected.Count; ordinal++)
            if (expected[ordinal] != actual[ordinal]) return false;
        return true;
    }
}
