using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Abstractions;

/// <summary>Immutable provider-neutral registered operation selected by source and stable business name.</summary>
public sealed record OperationPlan(
    string Source,
    string Name,
    DataOperationEffect Effect,
    OperationResultKind Result,
    OperationDelivery Delivery,
    IReadOnlyList<OperationParameter> Parameters,
    IDataOperationBinding Binding,
    string? LaneName,
    DataReadLanePlan? Lane,
    RecordSetLimits Limits,
    TimeSpan Timeout,
    Type? ScalarType = null);
