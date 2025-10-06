using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime.Contributors;

/// <summary>
/// Default contributor that resolves canonical aggregation keys and aligns canonical identifiers.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
internal sealed class DefaultAggregationContributor<TModel> : ICanonPipelineContributor<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    internal const string ExistingEntityContextKey = "canon:existing-entity";
    internal const string ExistingMetadataContextKey = "canon:existing-metadata";
    internal const string ArrivalTokenContextKey = "canon:arrival-token";

    private readonly CanonModelAggregationMetadata _metadata;
    private readonly string _entityType;
    private readonly IReadOnlyList<PropertyInfo> _keyProperties;

    public DefaultAggregationContributor(CanonModelAggregationMetadata metadata)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _entityType = metadata.ModelType.FullName ?? metadata.ModelType.Name;
        _keyProperties = metadata.KeyProperties;
    }

    /// <inheritdoc />
    public CanonPipelinePhase Phase => CanonPipelinePhase.Aggregation;

    /// <inheritdoc />
    public async ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<TModel> context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var arrivalToken = ResolveArrivalToken(context);
        context.SetItem(ArrivalTokenContextKey, arrivalToken);

        var compositeKey = BuildAggregationKey(context.Entity);
        var indexEntry = await context.Persistence.GetIndexAsync(_entityType, compositeKey, cancellationToken).ConfigureAwait(false);

        var canonicalId = await EnsureCanonicalIdAsync(context, indexEntry, cancellationToken).ConfigureAwait(false);
        var attributes = BuildIndexAttributes(context, arrivalToken);

        if (indexEntry is null)
        {
            indexEntry = new CanonIndex
            {
                EntityType = _entityType,
                Key = compositeKey,
                Kind = CanonIndexKeyKind.Aggregation
            };
        }

        indexEntry.Update(canonicalId, context.Metadata.Origin, attributes);
        await context.Persistence.UpsertIndexAsync(indexEntry, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private async Task<string> EnsureCanonicalIdAsync(CanonPipelineContext<TModel> context, CanonIndex? indexEntry, CancellationToken cancellationToken)
    {
        if (indexEntry is null)
        {
            var assignedId = context.Entity.Id;
            context.Metadata.AssignCanonicalId(assignedId);
            return assignedId;
        }

        var canonicalId = indexEntry.CanonicalId;
        if (!string.Equals(context.Entity.Id, canonicalId, StringComparison.OrdinalIgnoreCase))
        {
            context.Entity.Id = canonicalId;
        }

        context.Metadata.AssignCanonicalId(canonicalId);
        await AttachExistingSnapshotAsync(context, canonicalId, cancellationToken).ConfigureAwait(false);
        return canonicalId;
    }

    private async Task AttachExistingSnapshotAsync(CanonPipelineContext<TModel> context, string canonicalId, CancellationToken cancellationToken)
    {
        if (context.TryGetItem(ExistingEntityContextKey, out TModel? cachedEntity) && cachedEntity is not null)
        {
            var cachedMetadata = context.TryGetItem(ExistingMetadataContextKey, out CanonMetadata? cached) && cached is not null
                ? cached
                : cachedEntity.Metadata.Clone();

            ApplyExistingMetadata(context, cachedEntity, cachedMetadata, canonicalId);
            return;
        }

        TModel? existing;

        try
        {
            existing = await CanonEntity<TModel>.Get(canonicalId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (IsAppHostUnavailable(ex))
        {
            return;
        }

        if (existing is null)
        {
            return;
        }

        var snapshot = existing.Metadata.Clone();
        ApplyExistingMetadata(context, existing, snapshot, canonicalId);
    }

    private static void ApplyExistingMetadata(CanonPipelineContext<TModel> context, TModel entity, CanonMetadata snapshot, string canonicalId)
    {
        context.SetItem(ExistingEntityContextKey, entity);
        context.SetItem(ExistingMetadataContextKey, snapshot.Clone());

        var merged = snapshot.Clone();
        merged.Merge(context.Metadata, preferIncoming: true);
        merged.AssignCanonicalId(canonicalId);
        context.ApplyMetadata(merged);
    }

    private static bool IsAppHostUnavailable(InvalidOperationException exception)
        => exception.Message.IndexOf("AppHost.Current", StringComparison.OrdinalIgnoreCase) >= 0;

    private string ResolveArrivalToken(CanonPipelineContext<TModel> context)
    {
        if (!string.IsNullOrWhiteSpace(context.Options.CorrelationId))
        {
            return context.Options.CorrelationId!;
        }

        return context.Entity.Id;
    }

    private static IReadOnlyDictionary<string, string?> BuildIndexAttributes(CanonPipelineContext<TModel> context, string arrivalToken)
    {
        var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["arrivalToken"] = arrivalToken
        };

        if (!string.IsNullOrWhiteSpace(context.Metadata.Origin))
        {
            attributes["source"] = context.Metadata.Origin;
        }

        foreach (var source in context.Metadata.Sources)
        {
            var attribution = source.Value;
            var prefix = $"source.{source.Key}";
            attributes[$"{prefix}.displayName"] = attribution.DisplayName ?? attribution.Key;
            attributes[$"{prefix}.channel"] = attribution.Channel;
            attributes[$"{prefix}.seenAt"] = attribution.SeenAt.ToString("O", CultureInfo.InvariantCulture);

            foreach (var pair in attribution.Attributes)
            {
                attributes[$"{prefix}.attr.{pair.Key}"] = pair.Value;
            }
        }

        return attributes;
    }

    private string BuildAggregationKey(TModel entity)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < _keyProperties.Count; i++)
        {
            var property = _keyProperties[i];
            var value = property.GetValue(entity);
            if (value is null)
            {
                throw new InvalidOperationException($"Aggregation key property '{property.Name}' on '{_metadata.ModelType.Name}' produced a null value.");
            }

            if (i > 0)
            {
                builder.Append('|');
            }

            builder.Append(property.Name);
            builder.Append('=');
            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
