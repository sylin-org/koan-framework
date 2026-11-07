using Koan.Web.Authorization;

namespace Koan.Web.Hooks;

/// <summary>
/// Orchestrates hook invocation in deterministic order and handles short-circuiting. The controller answering a request runs as a hook of
/// that request where it implements one (WEB-0087), after the registered hooks and before the access floor, so two controllers over one entity
/// never run each other's.
/// </summary>
internal sealed class HookRunner<TEntity> where TEntity : class
{
    private readonly IEnumerable<IRequestOptionsHook<TEntity>> _opts;
    private readonly IEnumerable<ICollectionHook<TEntity>> _col;
    private readonly IEnumerable<IModelHook<TEntity>> _model;
    private readonly IEnumerable<IEmitHook<TEntity>> _emit;

    public HookRunner(IEnumerable<IRequestOptionsHook<TEntity>> opts,
                      IEnumerable<ICollectionHook<TEntity>> col,
                      IEnumerable<IModelHook<TEntity>> model,
                      IEnumerable<IEmitHook<TEntity>> emit)
    {
        _opts = opts.OrderBy(i => i.Order).ToArray();
        _col = col.OrderBy(i => i.Order).ToArray();
        _model = model.OrderBy(i => i.Order).ToArray();
        _emit = emit.OrderBy(i => i.Order).ToArray();
    }

    /// <summary>WEB-0077: whether any model hook is registered, so a bulk delete knows it must load its rows.</summary>
    public bool HasModelHooks => _model.Any();

    private static IEnumerable<THook> With<THook>(IEnumerable<THook> registered, HookContext<TEntity> ctx)
        => ctx.Request.Owner is THook own ? [.. registered, own] : registered;

    /// <summary>
    /// Allow hooks to mutate query options; returns false if short-circuited.
    /// </summary>
    public async Task<bool> BuildOptions(HookContext<TEntity> ctx, QueryOptions opts)
    {
        foreach (var h in With(_opts, ctx))
        {
            if (h is EntityAccessConstrainHook<TEntity>) continue;
            await h.OnBuildingOptions(ctx, opts);
            if (ctx.IsShortCircuited) break;
        }
        // The access floor also governs a hook's attempted success fallback. It is contributed
        // exactly once, after user options, even when those options short-circuit execution.
        foreach (var access in _opts.OfType<EntityAccessConstrainHook<TEntity>>())
            await access.OnBuildingOptions(ctx, opts);
        return !ctx.IsShortCircuited;
    }

    public async Task<bool> BeforeCollection(HookContext<TEntity> ctx, QueryOptions opts)
    {
        foreach (var h in With(_col, ctx))
        {
            await h.OnBeforeFetch(ctx, opts);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterCollection(HookContext<TEntity> ctx, List<TEntity> items)
    {
        foreach (var h in With(_col, ctx))
        {
            await h.OnAfterFetch(ctx, items);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforeModelFetch(HookContext<TEntity> ctx, string id)
    {
        foreach (var h in With(_model, ctx))
        {
            await h.OnBeforeFetch(ctx, id);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterModelFetch(HookContext<TEntity> ctx, TEntity? model)
    {
        foreach (var h in With(_model, ctx))
        {
            await h.OnAfterFetch(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforeSave(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in With(_model, ctx))
        {
            await h.OnBeforeSave(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterSave(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in With(_model, ctx))
        {
            await h.OnAfterSave(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforeDelete(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in With(_model, ctx))
        {
            await h.OnBeforeDelete(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterDelete(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in With(_model, ctx))
        {
            await h.OnAfterDelete(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforePatch(HookContext<TEntity> ctx, string id, object patch)
    {
        foreach (var h in With(_model, ctx))
        {
            await h.OnBeforePatch(ctx, id, patch);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterPatch(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in With(_model, ctx))
        {
            await h.OnAfterPatch(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    /// <summary>
    /// Run emit hooks for collections; allow replacement of payload or short-circuit.
    /// </summary>
    public async Task<(bool replaced, object payload)> EmitCollection(HookContext<TEntity> ctx, object payload)
    {
        var replaced = false;
        foreach (var h in With(_emit, ctx))
        {
            var decision = await h.OnEmitCollection(ctx, payload);
            if (ctx.IsShortCircuited) return (true, ctx.ShortCircuitPayload!);
            if (decision is EmitDecision.DeferredProjection) return (true, decision);
            if (decision is EmitDecision.Replace replacement)
            {
                payload = replacement.Payload;
                replaced = true;
            }
        }
        return (replaced, payload);
    }

    /// <summary>
    /// Run emit hooks for single models; allow replacement of payload or short-circuit.
    /// </summary>
    public async Task<(bool replaced, object payload)> EmitModel(HookContext<TEntity> ctx, object payload)
    {
        var replaced = false;
        foreach (var h in With(_emit, ctx))
        {
            var decision = await h.OnEmitModel(ctx, payload);
            if (ctx.IsShortCircuited) return (true, ctx.ShortCircuitPayload!);
            if (decision is EmitDecision.DeferredProjection)
                throw new NotSupportedException("Source-bound emit projection supports collection and query responses only.");
            if (decision is EmitDecision.Replace replacement)
            {
                payload = replacement.Payload;
                replaced = true;
            }
        }
        return (replaced, payload);
    }
}
