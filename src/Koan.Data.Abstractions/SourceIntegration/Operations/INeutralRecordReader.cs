namespace Koan.Data.Abstractions;

/// <summary>
/// One native result channel normalized incrementally into Data's closed value algebra.
/// Fields are fixed before the first read; completion facts are valid after <see cref="Read"/> returns null.
/// </summary>
public interface INeutralRecordReader : IAsyncDisposable
{
    IReadOnlyList<DataField> Fields { get; }
    NeutralRecordReaderCompletion Completion { get; }
    bool HasAdditionalResultChannels { get; }
    ValueTask<DataRecord?> Read(CancellationToken ct = default);
}
