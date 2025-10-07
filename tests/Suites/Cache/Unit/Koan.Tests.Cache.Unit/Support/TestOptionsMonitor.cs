using Microsoft.Extensions.Options;

namespace Koan.Tests.Cache.Unit.Support;

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    private readonly T _value;

    public TestOptionsMonitor(T value)
    {
        _value = value;
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
