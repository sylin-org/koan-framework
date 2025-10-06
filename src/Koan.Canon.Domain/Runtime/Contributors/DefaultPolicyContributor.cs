using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Audit;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime.Contributors;

/// <summary>
/// Applies declared aggregation policies and emits audit metadata.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
internal sealed class DefaultPolicyContributor<TModel> : ICanonPipelineContributor<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    internal const string AuditEntriesContextKey = "canon:audit-entries";

    private readonly CanonModelAggregationMetadata _metadata;
    private readonly string _entityType;

    public DefaultPolicyContributor(CanonModelAggregationMetadata metadata)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _entityType = metadata.ModelType.FullName ?? metadata.ModelType.Name;
    }

    /// <inheritdoc />
    public CanonPipelinePhase Phase => CanonPipelinePhase.Policy;

    /// <inheritdoc />
    public ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<TModel> context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (_metadata.PolicyByProperty.Count == 0 && !_metadata.AuditEnabled)
        {
            return ValueTask.FromResult<CanonizationEvent?>(null);
        }

        context.TryGetItem(DefaultAggregationContributor<TModel>.ExistingEntityContextKey, out TModel? existingEntity);
        context.TryGetItem(DefaultAggregationContributor<TModel>.ExistingMetadataContextKey, out CanonMetadata? existingMetadata);
        context.TryGetItem(DefaultAggregationContributor<TModel>.ArrivalTokenContextKey, out string? arrivalToken);

        arrivalToken ??= context.Entity.Id;

        var auditEntries = _metadata.AuditEnabled ? new List<CanonAuditEntry>() : null;
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _metadata.PolicyByProperty)
        {
            var property = pair.Key;
            var descriptor = pair.Value;
            var policy = descriptor.Kind;

            var incomingValue = property.GetValue(context.Entity);
            var existingValue = existingEntity is not null ? property.GetValue(existingEntity) : null;
            var existingFootprint = existingMetadata?.PropertyFootprints.GetValueOrDefault(property.Name);

            var evaluation = EvaluatePolicy(descriptor, incomingValue, existingValue, existingFootprint, now, arrivalToken, context.Metadata.Origin);
            evaluation.Evidence["incoming"] = FormatValue(incomingValue);
            evaluation.Evidence["existing"] = FormatValue(existingValue);
            evaluation.Evidence["selected"] = FormatValue(evaluation.SelectedValue);

            property.SetValue(context.Entity, evaluation.SelectedValue);

            var footprint = new CanonPropertyFootprint
            {
                Property = property.Name,
                SourceKey = evaluation.SourceKey,
                ArrivalToken = evaluation.ArrivalToken,
                ArrivedAt = evaluation.ArrivalAt,
                Value = FormatValue(evaluation.SelectedValue),
                Policy = policy.ToString(),
                Evidence = new Dictionary<string, string?>(evaluation.Evidence, StringComparer.OrdinalIgnoreCase)
            };
            context.Metadata.PropertyFootprints[property.Name] = footprint;

            var policySnapshot = new CanonPolicySnapshot
            {
                Policy = $"{property.Name}:{policy}",
                Outcome = evaluation.Outcome,
                AppliedAt = evaluation.ArrivalAt,
                Evidence = new Dictionary<string, string?>(evaluation.Evidence, StringComparer.OrdinalIgnoreCase)
            };
            context.Metadata.RecordPolicy(policySnapshot);

            if (_metadata.AuditEnabled && auditEntries is not null)
            {
                auditEntries.Add(new CanonAuditEntry
                {
                    CanonicalId = context.Metadata.CanonicalId ?? context.Entity.Id,
                    EntityType = _entityType,
                    Property = property.Name,
                    PreviousValue = FormatValue(existingValue),
                    CurrentValue = FormatValue(evaluation.SelectedValue),
                    Policy = policy.ToString(),
                    Source = evaluation.SourceKey,
                    ArrivalToken = evaluation.ArrivalToken,
                    OccurredAt = evaluation.ArrivalAt,
                    Evidence = new Dictionary<string, string?>(evaluation.Evidence, StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        if (auditEntries is { Count: > 0 })
        {
            context.SetItem(AuditEntriesContextKey, auditEntries);
        }

        return ValueTask.FromResult<CanonizationEvent?>(null);
    }

    private static PolicyEvaluationResult EvaluatePolicy(
        AggregationPolicyDescriptor descriptor,
        object? incomingValue,
        object? existingValue,
        CanonPropertyFootprint? existingFootprint,
        DateTimeOffset now,
        string arrivalToken,
        string? sourceKey)
    {
        return descriptor.Kind switch
        {
            AggregationPolicyKind.SourceOfTruth => EvaluateSourceOfTruth(descriptor, incomingValue, existingValue, existingFootprint, now, arrivalToken, sourceKey),
            _ => EvaluateSimplePolicy(descriptor.Kind, incomingValue, existingValue, existingFootprint, now, arrivalToken, sourceKey)
        };
    }

    private static PolicyEvaluationResult EvaluateSimplePolicy(
        AggregationPolicyKind policy,
        object? incomingValue,
        object? existingValue,
        CanonPropertyFootprint? existingFootprint,
        DateTimeOffset now,
        string arrivalToken,
        string? sourceKey)
    {
        return policy switch
        {
            AggregationPolicyKind.First => EvaluateFirst(incomingValue, existingValue, existingFootprint, now, arrivalToken, sourceKey),
            AggregationPolicyKind.Latest => EvaluateLatest(incomingValue, existingValue, existingFootprint, now, arrivalToken, sourceKey),
            AggregationPolicyKind.Min => EvaluateMinMax(incomingValue, existingValue, existingFootprint, now, arrivalToken, sourceKey, preferMin: true),
            AggregationPolicyKind.Max => EvaluateMinMax(incomingValue, existingValue, existingFootprint, now, arrivalToken, sourceKey, preferMin: false),
            _ => throw new InvalidOperationException($"Policy '{policy}' is not supported as a fallback.")
        };
    }

    private static PolicyEvaluationResult EvaluateSourceOfTruth(
        AggregationPolicyDescriptor descriptor,
        object? incomingValue,
        object? existingValue,
        CanonPropertyFootprint? existingFootprint,
        DateTimeOffset now,
        string arrivalToken,
        string? sourceKey)
    {
        var authoritativeIncoming = descriptor.IsAuthoritativeSource(sourceKey);
        var authoritativeExisting = existingFootprint is not null && descriptor.IsAuthoritativeSource(existingFootprint.SourceKey);

        if (authoritativeIncoming)
        {
            var result = PolicyEvaluationResult.FromIncoming(incomingValue, now, arrivalToken, sourceKey);
            result.Evidence["authority"] = "incoming";
            return result;
        }

        if (authoritativeExisting)
        {
            var result = PolicyEvaluationResult.FromExisting(existingValue, existingFootprint);
            result.Evidence["authority"] = "existing";
            return result;
        }

        var fallbackResult = EvaluateSimplePolicy(descriptor.Fallback, incomingValue, existingValue, existingFootprint, now, arrivalToken, sourceKey);
        fallbackResult.Evidence["authority"] = "fallback";
        fallbackResult.Evidence["fallbackPolicy"] = descriptor.Fallback.ToString();
        return fallbackResult;
    }

    private static PolicyEvaluationResult EvaluateFirst(object? incomingValue, object? existingValue, CanonPropertyFootprint? footprint, DateTimeOffset now, string arrivalToken, string? sourceKey)
    {
        if (existingValue is not null)
        {
            return PolicyEvaluationResult.FromExisting(existingValue, footprint);
        }

        return PolicyEvaluationResult.FromIncoming(incomingValue, now, arrivalToken, sourceKey);
    }

    private static PolicyEvaluationResult EvaluateLatest(object? incomingValue, object? existingValue, CanonPropertyFootprint? footprint, DateTimeOffset now, string arrivalToken, string? sourceKey)
    {
        var previousTimestamp = footprint?.ArrivedAt ?? DateTimeOffset.MinValue;
        var previousToken = footprint?.ArrivalToken ?? string.Empty;

        if (incomingValue is null && existingValue is null)
        {
            return PolicyEvaluationResult.FromExisting(null, footprint);
        }

        if (incomingValue is null)
        {
            return PolicyEvaluationResult.FromExisting(existingValue, footprint);
        }

        if (existingValue is null)
        {
            return PolicyEvaluationResult.FromIncoming(incomingValue, now, arrivalToken, sourceKey);
        }

        if (previousTimestamp > now)
        {
            return PolicyEvaluationResult.FromExisting(existingValue, footprint);
        }

        if (previousTimestamp == now && string.Compare(previousToken, arrivalToken, StringComparison.Ordinal) >= 0)
        {
            return PolicyEvaluationResult.FromExisting(existingValue, footprint);
        }

        return PolicyEvaluationResult.FromIncoming(incomingValue, now, arrivalToken, sourceKey);
    }

    private static PolicyEvaluationResult EvaluateMinMax(
        object? incomingValue,
        object? existingValue,
        CanonPropertyFootprint? footprint,
        DateTimeOffset now,
        string arrivalToken,
        string? sourceKey,
        bool preferMin)
    {
        if (incomingValue is null && existingValue is null)
        {
            return PolicyEvaluationResult.FromExisting(null, footprint);
        }

        if (incomingValue is null)
        {
            return PolicyEvaluationResult.FromExisting(existingValue, footprint);
        }

        if (existingValue is null)
        {
            return PolicyEvaluationResult.FromIncoming(incomingValue, now, arrivalToken, sourceKey);
        }

        var comparison = CompareValues(incomingValue, existingValue);
        var takeIncoming = preferMin ? comparison <= 0 : comparison >= 0;
        return takeIncoming
            ? PolicyEvaluationResult.FromIncoming(incomingValue, now, arrivalToken, sourceKey)
            : PolicyEvaluationResult.FromExisting(existingValue, footprint);
    }

    private static int CompareValues(object left, object right)
    {
        if (left.GetType() != right.GetType())
        {
            throw new InvalidOperationException($"Cannot compare values of type '{left.GetType().Name}' and '{right.GetType().Name}'.");
        }

        return left switch
        {
            IComparable comparable => comparable.CompareTo(right),
            _ => throw new InvalidOperationException($"Type '{left.GetType().Name}' does not implement IComparable and cannot be used with Min/Max policies.")
        };
    }

    private static string? FormatValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private readonly struct PolicyEvaluationResult
    {
        private PolicyEvaluationResult(object? selectedValue, string outcome, string? sourceKey, string arrivalToken, DateTimeOffset arrivalAt, Dictionary<string, string?> evidence)
        {
            SelectedValue = selectedValue;
            Outcome = outcome;
            SourceKey = sourceKey;
            ArrivalToken = arrivalToken;
            ArrivalAt = arrivalAt;
            Evidence = evidence;
        }

        public object? SelectedValue { get; }
        public string Outcome { get; }
        public string? SourceKey { get; }
        public string ArrivalToken { get; }
        public DateTimeOffset ArrivalAt { get; }
        public Dictionary<string, string?> Evidence { get; }

        public static PolicyEvaluationResult FromIncoming(object? value, DateTimeOffset arrivalAt, string arrivalToken, string? sourceKey)
        {
            var evidence = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["winner"] = "incoming",
                ["arrivalToken"] = arrivalToken
            };

            return new PolicyEvaluationResult(value, "incoming", sourceKey, arrivalToken, arrivalAt, evidence);
        }

        public static PolicyEvaluationResult FromExisting(object? value, CanonPropertyFootprint? footprint)
        {
            var evidence = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["winner"] = "existing"
            };

            if (footprint is not null)
            {
                evidence["arrivalToken"] = footprint.ArrivalToken;
            }

            return new PolicyEvaluationResult(value, "existing", footprint?.SourceKey, footprint?.ArrivalToken ?? string.Empty, footprint?.ArrivedAt ?? DateTimeOffset.MinValue, evidence);
        }
    }
}
