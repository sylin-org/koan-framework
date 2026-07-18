using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Abstractions;

public sealed record VectorQueryOptions(
    float[] Query,
    int TopK = VectorQueryOptions.DefaultTopK,
    string? ContinuationToken = null,
    // AI-0036 §9 / DATA-0097 P1: the typed, unified Filter slot (was object?). The Vector<T>/workflow
    // facades parse string/dict/JSON into this once via VectorFilterReader; VectorFilterCoordinator
    // then validates it (residual-is-error) before any repo sees it.
    Filter? Filter = null,
    TimeSpan? Timeout = null,
    string? VectorName = null,
    string? SearchText = null,
    double? Alpha = null)
{
    public const int DefaultTopK = 10;

    private int _topK = ValidateTopK(TopK);

    public int TopK
    {
        get => _topK;
        init => _topK = ValidateTopK(value);
    }

    private static int ValidateTopK(int value) => value > 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(TopK), value, "Vector TopK must be greater than zero.");
}
