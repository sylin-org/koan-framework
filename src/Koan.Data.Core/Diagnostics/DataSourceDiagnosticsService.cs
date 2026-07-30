using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Mapping.Composition;
using Koan.Data.Core.Options;
using Koan.Data.Core.SourceIntegration.Runtime;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Diagnostics;

/// <summary>Projects pure diagnostics and coordinates explicitly non-mutating provider checks.</summary>
internal sealed class DataSourceDiagnosticsService
{
    private readonly DataOperationCatalog _operations;
    private readonly MappingDeclarationCatalog _mappings;
    private readonly TimeSpan _doctorTimeout;
    private readonly DataNativeEvidenceStore _evidence;

    public DataSourceDiagnosticsService(
        DataOperationCatalog operations,
        MappingDeclarationCatalog mappings,
        DataNativeEvidenceStore evidence,
        IOptions<SourceIntegrationOptions> options)
    {
        _operations = operations;
        _mappings = mappings;
        _evidence = evidence;
        _doctorTimeout = options.Value.DoctorTimeout;
        if (_doctorTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Koan:Data:SourceIntegration:DoctorTimeout must be positive.");
    }

    public DataSourceDescription Describe(ResolvedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var operations = _operations.Snapshot()
            .Where(plan => SameSource(plan.Source, source.Plan.Source))
            .Select(plan => Describe(plan, source.Descriptor))
            .ToArray();
        var mappings = _mappings.Snapshot()
            .Where(descriptor => SameSource(descriptor.Source, source.Plan.Source))
            .Select(static descriptor => new DataMappingDescription(
                MappingPlanCompiler.Identify(descriptor),
                descriptor.Identity.Parts.Count,
                descriptor.Bindings.Count(static binding => binding.Shape == MappingValueShape.Scalar),
                descriptor.Bindings.Count(static binding => binding.Shape == MappingValueShape.Object),
                descriptor.Bindings.Any(static binding => binding.PhysicalPath.IsNested)))
            .ToArray();

        return new DataSourceDescription(
            source.Plan.Source,
            source.Provider,
            source.Plan.RouteIdentity,
            source.Plan.StorageLifecycle,
            source.Plan.Access,
            source.Plan.ReadLanes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            source.Claims.Capabilities,
            source.Claims.Claims,
            source.Descriptor.Operations,
            source.Descriptor.Inspection,
            operations,
            mappings);
    }

    public DataSourceExplanation Explain(ResolvedSource source, string operation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var plan = _operations.Require(source.Plan.Source, operation);
        return new DataSourceExplanation(
            source.Plan.RouteIdentity,
            source.Provider,
            Describe(plan, source.Descriptor),
            source.Claims.Claims.Select(static claim => claim.Reference).ToArray());
    }

    public async Task<DataSourceDiagnosis> Doctor(ResolvedSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var references = source.Claims.Claims.Select(static claim => claim.Reference).ToArray();
        if (!source.Descriptor.SupportsDoctor)
            return Diagnosis(source, references,
                new DataDoctorCheck(DataDoctorCodes.Unsupported, DataDoctorStatus.Unavailable));

        IDataSourceDoctor doctor;
        try
        {
            if (source.Integration is not IDataSourceDoctor available)
                return Diagnosis(source, references,
                    new DataDoctorCheck(DataDoctorCodes.ContractMismatch, DataDoctorStatus.Failed));
            doctor = available;
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            return Diagnosis(source, references,
                new DataDoctorCheck(DataDoctorCodes.NativeFailure, DataDoctorStatus.Failed,
                    Record(source, error, "doctor.activate")));
        }

        using var timeout = new CancellationTokenSource(_doctorTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var receipt = await doctor.Doctor(linked.Token).ConfigureAwait(false);
            if (receipt is null)
                return Diagnosis(source, references,
                    new DataDoctorCheck(DataDoctorCodes.ContractMismatch, DataDoctorStatus.Failed));
            return Diagnosis(source, references, receipt.Checks.ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Diagnosis(source, references,
                new DataDoctorCheck(DataDoctorCodes.Timeout, DataDoctorStatus.TimedOut));
        }
        catch (Exception error)
        {
            return Diagnosis(source, references,
                new DataDoctorCheck(DataDoctorCodes.NativeFailure, DataDoctorStatus.Failed,
                    Record(source, error, "doctor.execute")));
        }
    }

    private static DataOperationDescription Describe(OperationPlan plan, DataSourceIntegrationDescriptor descriptor)
    {
        var supported = descriptor.Supports(plan.Binding, plan.Result, plan.Lane);
        return new DataOperationDescription(
            plan.Name,
            plan.Effect,
            plan.Result,
            plan.Delivery,
            plan.Binding.Kind,
            plan.Parameters.Count,
            plan.Limits,
            plan.Timeout,
            supported ? DataOperationSupport.Supported : DataOperationSupport.Unsupported,
            supported ? null :
                "Select an adapter that explicitly declares this binding, result, and read-lane contract.");
    }

    private static DataSourceDiagnosis Diagnosis(
        ResolvedSource source,
        IReadOnlyList<string> references,
        params DataDoctorCheck[] checks)
    {
        var findings = checks.Select(check => new DataDoctorFinding(
            check.Code,
            check.Status,
            Correction(check.Code),
            check.EvidenceReference)).ToArray();
        return new DataSourceDiagnosis(
            source.Plan.RouteIdentity,
            source.Provider,
            Aggregate(checks.Select(static check => check.Status)),
            references,
            findings);
    }

    private static DataDoctorStatus Aggregate(IEnumerable<DataDoctorStatus> statuses)
    {
        var result = DataDoctorStatus.Healthy;
        foreach (var status in statuses)
        {
            if (Rank(status) > Rank(result)) result = status;
        }
        return result;
    }

    private static int Rank(DataDoctorStatus status) => status switch
    {
        DataDoctorStatus.Healthy => 0,
        DataDoctorStatus.Degraded => 1,
        DataDoctorStatus.Unavailable => 2,
        DataDoctorStatus.TimedOut => 3,
        DataDoctorStatus.Failed => 4,
        _ => 4
    };

    private static string? Correction(string code) => code switch
    {
        DataDoctorCodes.Unsupported => "Use an adapter that declares a safe Doctor probe; Koan will not guess a native operation.",
        DataDoctorCodes.ContractMismatch => "Align the adapter's source descriptor with its IDataSourceDoctor implementation.",
        DataDoctorCodes.NativeFailure => "Inspect the restricted native evidence through an authorized diagnostic channel.",
        DataDoctorCodes.Timeout => "Increase DoctorTimeout deliberately or correct provider reachability; caller cancellation is reported separately.",
        _ => null
    };

    private static bool SameSource(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private string Record(ResolvedSource source, Exception error, string operation) =>
        _evidence.Record(
            error,
            new DataNativeEvidenceContext(source.Provider, DataNativeTargetKind.Source, operation));
}
