using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Abstractions;

/// <summary>Redacted description of one immutable registered operation.</summary>
public sealed record DataOperationDescription(
    string Name,
    DataOperationEffect Effect,
    OperationResultKind Result,
    OperationDelivery Delivery,
    string Binding,
    int ParameterCount,
    RecordSetLimits Bounds,
    TimeSpan Timeout,
    DataOperationSupport Support,
    string? Correction);
