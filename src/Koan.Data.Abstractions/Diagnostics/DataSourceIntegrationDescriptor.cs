using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Abstractions;

/// <summary>Pure source-integration declaration available without creating a provider client or integration.</summary>
public sealed class DataSourceIntegrationDescriptor
{
    public static DataSourceIntegrationDescriptor Empty { get; } = new();

    public DataSourceIntegrationDescriptor(
        SourceIntegrationCapabilities operations = SourceIntegrationCapabilities.None,
        SourceInspectionCapabilities inspection = SourceInspectionCapabilities.None,
        IEnumerable<string>? bindingKinds = null,
        bool enforcesReadLanes = false,
        bool supportsDoctor = false)
    {
        Operations = operations;
        Inspection = inspection;
        BindingKinds = (bindingKinds ?? [])
            .Select(static kind => string.IsNullOrWhiteSpace(kind)
                ? throw new ArgumentException("A source-integration binding kind cannot be empty.", nameof(bindingKinds))
                : kind.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        EnforcesReadLanes = enforcesReadLanes;
        SupportsDoctor = supportsDoctor;
    }

    public SourceIntegrationCapabilities Operations { get; }
    public SourceInspectionCapabilities Inspection { get; }
    public IReadOnlyList<string> BindingKinds { get; }
    public bool EnforcesReadLanes { get; }
    public bool SupportsDoctor { get; }

    public bool Supports(IDataOperationBinding binding, OperationResultKind result, DataReadLanePlan? lane)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var resultSupported = result switch
        {
            OperationResultKind.Records => Operations.HasFlag(SourceIntegrationCapabilities.RegisteredRecords),
            OperationResultKind.Scalar => Operations.HasFlag(SourceIntegrationCapabilities.RegisteredScalar),
            _ => false
        };
        return resultSupported &&
               BindingKinds.Contains(binding.Kind, StringComparer.OrdinalIgnoreCase) &&
               (lane is null || EnforcesReadLanes);
    }
}
