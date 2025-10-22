using System;
using System.Collections.Generic;
using System.Threading;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Cache.Scope;

internal sealed class CacheScopeAccessor : ICacheScopeAccessor
{
    private readonly AsyncLocal<Stack<CacheScopeContext>?> _scopes = new();

    public CacheScopeContext Current
    {
        get
        {
            var stack = _scopes.Value;
            if (stack is null || stack.Count == 0)
            {
                return CacheScopeContext.Empty;
            }

            return stack.Peek();
        }
    }

    public CacheScopeContext Push(string scopeId, string? region)
    {
        var stack = _scopes.Value ??= new Stack<CacheScopeContext>();
        var context = new CacheScopeContext(scopeId, region);
        stack.Push(context);
        return context;
    }

    public void Pop(CacheScopeContext context)
    {
        var stack = _scopes.Value;
        if (stack is null || stack.Count == 0)
        {
            return;
        }

        var current = stack.Pop();
        if (!ReferenceEquals(current, context) && (current.ScopeId != context.ScopeId || current.Region != context.Region))
        {
            // Put it back to avoid corruption if mismatched pop occurs.
            stack.Push(current);
            throw new InvalidOperationException("Attempted to pop a cache scope that is not the current scope.");
        }

        if (stack.Count == 0)
        {
            _scopes.Value = null;
        }
    }
}
