using System;

namespace Koan.Cache.Abstractions.Primitives;

public readonly struct CacheScopeHandle : IDisposable
{
    private readonly Action? _onDispose;

    public CacheScopeHandle(string scopeId, string? region, Action? onDispose)
    {
        ScopeId = scopeId;
        Region = region;
        _onDispose = onDispose;
    }

    public string ScopeId { get; }

    public string? Region { get; }

    public void Dispose()
    {
        _onDispose?.Invoke();
    }
}
