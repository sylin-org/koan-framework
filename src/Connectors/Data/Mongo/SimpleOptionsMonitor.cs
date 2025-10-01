using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// Simple IOptionsMonitor implementation for source-specific options.
/// </summary>
internal sealed class SimpleOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    private readonly T _value;

    public SimpleOptionsMonitor(T value)
    {
        _value = value;
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
