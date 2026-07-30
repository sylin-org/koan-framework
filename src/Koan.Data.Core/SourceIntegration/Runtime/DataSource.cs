using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core.Options;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core;

/// <summary>Provider-neutral source-first Data entry point.</summary>
public static class Data
{
    public static DataSource Source(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var options = AppHost.GetRequiredService<IOptions<SourceIntegrationOptions>>(
            "source integration options").Value;
        Validate(options);
        var integrations = AppHost.GetRequiredService<SourceIntegration.Runtime.DataSourceIntegrationService>(
            "source data access");
        var resolved = integrations.Resolve(name);
        return new DataSource(
            resolved,
            AppHost.GetRequiredService<SourceIntegration.Runtime.RegisteredOperationExecutor>("registered Data operation"),
            AppHost.GetRequiredService<SourceIntegration.Runtime.RecordSetMaterializer>("source record materialization"),
            AppHost.GetRequiredService<SourceIntegration.Runtime.SourceContinuationCodec>("source continuation"),
            AppHost.GetRequiredService<Diagnostics.DataSourceDiagnosticsService>("source diagnostics"),
            options);
    }

    private static void Validate(SourceIntegrationOptions options)
    {
        var limits = new RecordSetLimits(
            options.MaxRecords,
            options.MaxBytes,
            options.MaxValueBytes,
            options.MaxDuration);
        try
        {
            limits.Validate();
            if (options.ParameterPlanCacheEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.ParameterPlanCacheEntries));
            if (options.DoctorTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options.DoctorTimeout));
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new InvalidOperationException(
                "Koan:Data:SourceIntegration limits and ParameterPlanCacheEntries must all be positive.",
                error);
        }
    }
}

/// <summary>One immutable runtime handle for a configured source.</summary>
public sealed class DataSource
{
    private readonly SourceIntegration.Runtime.ResolvedSource _source;
    private readonly SourceIntegration.Runtime.RegisteredOperationExecutor _operations;
    private readonly SourceIntegration.Runtime.RecordSetMaterializer _materializer;
    private readonly SourceIntegration.Runtime.SourceContinuationCodec _continuations;
    private readonly Diagnostics.DataSourceDiagnosticsService _diagnostics;
    private readonly SourceIntegrationOptions _options;

    internal DataSource(
        SourceIntegration.Runtime.ResolvedSource source,
        SourceIntegration.Runtime.RegisteredOperationExecutor operations,
        SourceIntegration.Runtime.RecordSetMaterializer materializer,
        SourceIntegration.Runtime.SourceContinuationCodec continuations,
        Diagnostics.DataSourceDiagnosticsService diagnostics,
        SourceIntegrationOptions options)
    {
        _source = source;
        _operations = operations;
        _materializer = materializer;
        _continuations = continuations;
        _diagnostics = diagnostics;
        _options = options;
    }

    public string Name => _source.Plan.Source;

    /// <summary>Pure description of the exact frozen source decision; does not activate the adapter.</summary>
    public DataSourceDescription Describe() => _diagnostics.Describe(_source);

    /// <summary>Pure explanation of one registered operation; does not activate the adapter.</summary>
    public DataSourceExplanation Explain(string operation) => _diagnostics.Explain(_source, operation);

    /// <summary>Runs only the adapter's explicitly declared non-mutating diagnostic checks.</summary>
    public Task<DataSourceDiagnosis> Doctor(CancellationToken ct = default) => _diagnostics.Doctor(_source, ct);

    public IDataSourceInspector Inspect() => new SourceIntegration.Runtime.DataSourceInspector(
        _source,
        _materializer,
        _continuations,
        _options);

    public Task<RecordSet> Query(string name, CancellationToken ct = default) =>
        _operations.Query(_source, name, null, ct);

    public Task<RecordSet> Query(string name, object? parameters, CancellationToken ct = default) =>
        _operations.Query(_source, name, parameters, ct);

    public Task<T> Scalar<T>(string name, CancellationToken ct = default) =>
        _operations.Scalar<T>(_source, name, null, ct);

    public Task<T> Scalar<T>(string name, object? parameters, CancellationToken ct = default) =>
        _operations.Scalar<T>(_source, name, parameters, ct);
}
