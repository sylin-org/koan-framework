using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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

        var aggregationKey = BuildAggregationKey(context.Entity);
        var indexLookup = await LoadIndexesAsync(context, aggregationKey, cancellationToken);

        var canonicalId = await EnsureCanonicalIdAsync(context, indexLookup, cancellationToken);
        var attributes = BuildIndexAttributes(context, arrivalToken);

        foreach (var entry in indexLookup)
        {
            var index = entry.Value ?? new CanonIndex
            {
                EntityType = _entityType,
                Key = entry.Key,
                Kind = CanonIndexKeyKind.Aggregation
            };

            index.Update(canonicalId, context.Metadata.Origin, attributes);
            await context.Persistence.UpsertIndexAsync(index, cancellationToken);
        }

        return null;
    }

    private async Task<string> EnsureCanonicalIdAsync(
        CanonPipelineContext<TModel> context,
        IReadOnlyDictionary<string, CanonIndex?> indexLookup,
        CancellationToken cancellationToken)
    {
        var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in indexLookup.Values)
        {
            if (!string.IsNullOrWhiteSpace(index?.CanonicalId))
            {
                candidateIds.Add(index!.CanonicalId);
            }
        }

        if (candidateIds.Count == 0)
        {
            var assignedId = context.Entity.Id;
            context.Metadata.AssignCanonicalId(assignedId);
            return assignedId;
        }

        var canonicalId = candidateIds.OrderBy(static id => id, StringComparer.Ordinal).First();
        if (!string.Equals(context.Entity.Id, canonicalId, StringComparison.OrdinalIgnoreCase))
        {
            context.Entity.Id = canonicalId;
        }

        context.Metadata.AssignCanonicalId(canonicalId);
        await AttachExistingSnapshotAsync(context, canonicalId, cancellationToken);

        if (candidateIds.Count > 1)
        {
            var merged = candidateIds
                .Where(id => !string.Equals(id, canonicalId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (merged.Length > 0)
            {
                foreach (var absorbed in merged)
                {
                    context.Metadata.Lineage.MarkSuperseded(absorbed, "identity-union");
                }

                context.Metadata.Lineage.RecordMetadataUpdate($"identity-union:{string.Join(',', merged)}");
                var mergedSet = new HashSet<string>(merged, StringComparer.OrdinalIgnoreCase);
                if (context.Metadata.TryGetTag("identity:merged-from", out var existingTag) && !string.IsNullOrWhiteSpace(existingTag))
                {
                    foreach (var token in existingTag.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var value = token.Trim();
                        if (!string.IsNullOrEmpty(value))
                        {
                            mergedSet.Add(value);
                        }
                    }
                }

                context.Metadata.SetTag("identity:merged-from", string.Join(',', mergedSet));
            }
        }

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
            existing = await CanonEntity<TModel>.Get(canonicalId, cancellationToken);
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

    private async ValueTask<IReadOnlyDictionary<string, CanonIndex?>> LoadIndexesAsync(
        CanonPipelineContext<TModel> context,
        AggregationKey aggregationKey,
        CancellationToken cancellationToken)
    {
        var lookup = new Dictionary<string, CanonIndex?>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in aggregationKey.Tokens)
        {
            var index = await context.Persistence.GetIndexAsync(_entityType, token, cancellationToken);
            lookup[token] = index;
        }

        if (!string.IsNullOrWhiteSpace(aggregationKey.CompositeKey) && !lookup.ContainsKey(aggregationKey.CompositeKey!))
        {
            var composite = await context.Persistence.GetIndexAsync(_entityType, aggregationKey.CompositeKey!, cancellationToken);
            lookup[aggregationKey.CompositeKey!] = composite;
        }

        return lookup;
    }

    private AggregationKey BuildAggregationKey(TModel entity)
    {
        var tokens = new List<string>(_keyProperties.Count);
        foreach (var property in _keyProperties)
        {
            var value = property.GetValue(entity);
            if (value is null)
            {
                continue;
            }

            var formatted = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                continue;
            }

            tokens.Add($"{property.Name}={formatted}");
        }

        if (tokens.Count == 0)
        {
            var declared = string.Join(", ", _metadata.AggregationKeyNames);
            throw new InvalidOperationException($"Canonical entity '{_metadata.ModelType.Name}' requires at least one aggregation key value; all declared keys were null or empty ({declared}).");
        }

        var composite = tokens.Count > 1 ? string.Join('|', tokens) : null;
        return new AggregationKey(tokens, composite);
    }

    private readonly struct AggregationKey
    {
        public AggregationKey(IReadOnlyList<string> tokens, string? compositeKey)
        {
            Tokens = tokens;
            CompositeKey = compositeKey;
        }

        public IReadOnlyList<string> Tokens { get; }
        public string? CompositeKey { get; }
    }
}
