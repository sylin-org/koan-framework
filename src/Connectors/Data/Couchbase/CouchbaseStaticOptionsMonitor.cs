using System;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Couchbase;

/// <summary>
/// A fixed <see cref="IOptionsMonitor{T}"/> for a per-source (Database-mode) adapter instance — the source's resolved
/// <see cref="CouchbaseOptions"/> never change after the factory builds them, so there is nothing to monitor. Mirrors the
/// Mongo connector's source-options monitor (ARCH-0103 §L: a shared StaticOptionsMonitor is the eventual hoist target).
/// </summary>
internal sealed class CouchbaseStaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    where T : class
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
