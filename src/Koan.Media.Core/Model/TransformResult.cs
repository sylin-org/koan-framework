namespace Koan.Media.Core.Model;

/// <summary>
/// Materialized transformation result that can be branched for creating multiple derivatives
/// </summary>
public sealed class TransformResult : IAsyncDisposable
{
    private readonly MemoryStream _materializedStream;
    private readonly string _contentType;
    private bool _disposed;

    internal TransformResult(Stream source, string contentType)
    {
        _materializedStream = new MemoryStream();
        source.CopyTo(_materializedStream);
        source.Dispose();  // Dispose source after materialization
        _materializedStream.Position = 0;
        _contentType = contentType;
    }

    /// <summary>
    /// Create independent branch from materialized result for further transformations
    /// Each branch is an independent copy that can be transformed separately
    /// </summary>
    public Stream Branch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _materializedStream.Position = 0;
        var clone = new MemoryStream((int)_materializedStream.Length);
        _materializedStream.CopyTo(clone);
        clone.Position = 0;
        return clone;
    }

    /// <summary>
    /// Get the content type of the materialized result
    /// </summary>
    public string ContentType => _contentType;

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _materializedStream.DisposeAsync();
            _disposed = true;
        }
    }
}
