namespace Koan.Data.Abstractions.Failures;

/// <summary>
/// Adapter seam for translating native exception types and codes into Data-owned failure semantics.
/// Implementations must not classify by message text.
/// </summary>
public interface IDataFailureClassifier
{
    bool TryClassify(Exception nativeFailure, DataFailureContext context, out DataFailure failure);
}
