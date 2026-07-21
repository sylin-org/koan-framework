namespace Koan.Core.Context;

/// <summary>A hard segmentation obligation could not be faithfully carried across an async boundary.</summary>
public sealed class SegmentationContextException : InvalidOperationException
{
    internal SegmentationContextException(
        FailureKind failure,
        string dimensionId,
        string operation,
        string message)
        : base(message)
    {
        Failure = failure;
        DimensionId = dimensionId;
        Operation = operation;
    }

    public FailureKind Failure { get; }

    public string DimensionId { get; }

    public string Operation { get; }

    internal static SegmentationContextException MissingCarrier(string dimensionId, string operation)
        => new(
            FailureKind.MissingCarrier,
            dimensionId,
            operation,
            $"{operation} cannot preserve isolation context '{dimensionId}' because no context carrier declares it. " +
            "Reference or enable the capability that carries this hard dimension.");

    internal static SegmentationContextException MissingCapturedAxis(string dimensionId, string operation)
        => new(
            FailureKind.MissingCapturedAxis,
            dimensionId,
            operation,
            $"{operation} requires carried isolation context '{dimensionId}', but the corresponding axis is absent. " +
            "Establish the required context before starting the operation.");

    public enum FailureKind
    {
        MissingCarrier,
        MissingCapturedAxis
    }
}
