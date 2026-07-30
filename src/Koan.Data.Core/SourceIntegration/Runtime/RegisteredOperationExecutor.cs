using System.Diagnostics;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Options;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.SourceIntegration.Runtime;

internal sealed class RegisteredOperationExecutor
{
    private static readonly ActivitySource Telemetry = new("Koan.Data.SourceIntegration", "1.0.0");
    private readonly DataOperationCatalog _catalog;
    private readonly RecordSetMaterializer _materializer;
    private readonly OperationParameterBinder _parameters;
    private readonly SegmentationPlan _segmentation;

    public RegisteredOperationExecutor(
        DataOperationCatalog catalog,
        RecordSetMaterializer materializer,
        SegmentationPlan segmentation,
        IOptions<SourceIntegrationOptions> options)
    {
        _catalog = catalog;
        _materializer = materializer;
        _segmentation = segmentation;
        _parameters = new OperationParameterBinder(options);
    }

    public async Task<RecordSet> Query(
        ResolvedSource source,
        string name,
        object? parameters,
        CancellationToken ct)
    {
        var plan = Prepare(source, name, OperationResultKind.Records, scalarType: null, parameters, out var bound);
        using var activity = Start(source, plan);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var reader = await WithTimeout(
                    plan,
                    ct,
                    token => source.Integration.ExecuteRecords(plan, bound, token))
                .ConfigureAwait(false);
            if (reader is null)
                throw Reject(plan, "The provider returned no neutral record reader.");
            var result = await _materializer.Materialize(reader, plan.Limits, plan.Name, ct).ConfigureAwait(false);
            Complete(activity, started, result.Records.Count);
            return result;
        }
        catch (Exception error)
        {
            Fail(activity, started, error);
            throw;
        }
    }

    public async Task<T> Scalar<T>(
        ResolvedSource source,
        string name,
        object? parameters,
        CancellationToken ct)
    {
        var plan = Prepare(source, name, OperationResultKind.Scalar, typeof(T), parameters, out var bound);
        using var activity = Start(source, plan);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await WithTimeout(
                    plan,
                    ct,
                    token => source.Integration.ExecuteScalar(plan, bound, token))
                .ConfigureAwait(false);
            if (result is null) throw Reject(plan, "The provider returned no scalar result.");
            if (result.HasAdditionalResultChannels)
                throw new AdditionalResultChannelsNotSupportedException(plan.Name);
            if (result.RecordCount != 1 || result.FieldCount != 1)
                throw new ScalarCardinalityException(plan.Name, result.RecordCount, result.FieldCount);

            var field = new DataField(0, "value", providerTypeName: result.ProviderTypeName);
            var record = new DataRecord([field], [result.Value]);
            _ = record.TryGetValue(0, out var neutral);
            if (RecordSetAccounting.MeasurePresentValue(neutral) > plan.Limits.MaxValueBytes)
                throw Reject(plan, $"The scalar exceeds MaxValueBytes={plan.Limits.MaxValueBytes}.");
            if (Stopwatch.GetElapsedTime(started) >= plan.Limits.MaxDuration)
                throw new TimeoutException(
                    $"Scalar materialization for registered operation '{plan.Name}' exceeded its duration bound.");

            var converted = record.Get<T>(0);
            Complete(activity, started, 1);
            return converted;
        }
        catch (Exception error)
        {
            Fail(activity, started, error);
            throw;
        }
    }

    private OperationPlan Prepare(
        ResolvedSource source,
        string name,
        OperationResultKind result,
        Type? scalarType,
        object? values,
        out IReadOnlyList<BoundOperationParameter> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var declared = _catalog.Require(source.Plan.Source, name.Trim());
        if (!string.Equals(declared.Source, source.Plan.Source, StringComparison.OrdinalIgnoreCase))
            throw Reject(declared, "The operation belongs to a different source.");
        if (declared.Effect != DataOperationEffect.Read ||
            declared.Result != result ||
            declared.Delivery != OperationDelivery.Buffered ||
            (result == OperationResultKind.Scalar && declared.ScalarType != scalarType))
            throw Reject(
                declared,
                $"Expected Read + {result} + Buffered with the registered scalar type.");

        source.Plan.Demand(declared.Effect, $"registered operation '{declared.Name}'");
        if (!_segmentation.IsEmpty)
            throw Reject(
                declared,
                "Active segmentation requires an explicit host/control-plane scope, which this application surface does not expose.");

        DataReadLanePlan? lane = null;
        if (declared.LaneName is not null &&
            !source.Plan.ReadLanes.TryGetValue(declared.LaneName, out lane))
            throw Reject(
                declared,
                $"Read lane '{declared.LaneName}' is not configured on source '{source.Plan.Source}'.");
        if (declared.Binding.EffectProof is not (
                OperationBindingEffectProof.Opaque or OperationBindingEffectProof.ValidatedRead))
            throw Reject(declared, "The binding effect is Unknown.");
        if (declared.Binding.EffectProof == OperationBindingEffectProof.Opaque && lane is null)
            throw Reject(declared, "An opaque binding requires a permanently selected provider-enforced read lane.");

        var resolved = declared with { Lane = lane };
        parameters = _parameters.Bind(resolved, values);
        var integration = source.Integration;
        if (lane is not null && !integration.EnforcesReadLane(lane))
            throw Reject(
                declared,
                $"Adapter '{source.Provider}' cannot prove enforcement of read lane '{lane.Name}'.");
        if ((declared.Result == OperationResultKind.Records &&
             (integration.Capabilities & SourceIntegrationCapabilities.RegisteredRecords) == 0) ||
            (declared.Result == OperationResultKind.Scalar &&
             (integration.Capabilities & SourceIntegrationCapabilities.RegisteredScalar) == 0))
            throw Reject(declared, $"Adapter '{source.Provider}' does not support {declared.Result} operations.");
        if (!integration.Supports(declared.Binding, declared.Result))
            throw Reject(
                declared,
                $"Adapter '{source.Provider}' does not support binding kind '{declared.Binding.Kind}' for {declared.Result}.");
        return resolved;
    }

    private static async Task<T> WithTimeout<T>(
        OperationPlan plan,
        CancellationToken caller,
        Func<CancellationToken, Task<T>> dispatch)
    {
        caller.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(plan.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        try
        {
            return await dispatch(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!caller.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Registered operation '{plan.Name}' exceeded its provider execution timeout.",
                error);
        }
    }

    private static RegisteredOperationException Reject(OperationPlan plan, string correction) =>
        new(plan.Source, plan.Name, correction);

    private static Activity? Start(ResolvedSource source, OperationPlan plan)
    {
        var activity = Telemetry.StartActivity("registered-operation", ActivityKind.Client);
        activity?.SetTag("koan.data.source", source.Plan.Source);
        activity?.SetTag("koan.data.operation", plan.Name);
        activity?.SetTag("koan.data.provider", source.Provider);
        activity?.SetTag("koan.data.effect", plan.Effect.ToString());
        activity?.SetTag("koan.data.result", plan.Result.ToString());
        activity?.SetTag("koan.data.attempts", 1);
        return activity;
    }

    private static void Complete(Activity? activity, long started, int count)
    {
        activity?.SetTag("koan.data.result_count", count);
        activity?.SetTag("koan.data.duration_ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private static void Fail(Activity? activity, long started, Exception error)
    {
        activity?.SetTag("koan.data.duration_ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        activity?.SetTag("error.type", error.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
