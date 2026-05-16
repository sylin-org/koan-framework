using System;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Cache.Topology.Support;

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> stub returning a fixed value. Test only.</summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
