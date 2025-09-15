namespace Koan.Web.Hooks;

/// <summary>
/// Orchestrates hook invocation in deterministic order and handles short-circuiting.
/// </summary>
internal sealed class HookRunner<TEntity>
{
    private readonly IEnumerable<IAuthorizeHook<TEntity>> _auth;
    private readonly IEnumerable<IRequestOptionsHook<TEntity>> _opts;
    private readonly IEnumerable<ICollectionHook<TEntity>> _col;
    private readonly IEnumerable<IModelHook<TEntity>> _model;
    private readonly IEnumerable<IEmitHook<TEntity>> _emit;

    public HookRunner(IEnumerable<IAuthorizeHook<TEntity>> auth,
                      IEnumerable<IRequestOptionsHook<TEntity>> opts,
                      IEnumerable<ICollectionHook<TEntity>> col,
                      IEnumerable<IModelHook<TEntity>> model,
                      IEnumerable<IEmitHook<TEntity>> emit)
    {
        _auth = auth.OrderBy(i => i.Order).ToArray();
        _opts = opts.OrderBy(i => i.Order).ToArray();
        _col = col.OrderBy(i => i.Order).ToArray();
        _model = model.OrderBy(i => i.Order).ToArray();
        _emit = emit.OrderBy(i => i.Order).ToArray();
    }

    /// <summary>
    /// Run authorization hooks until a non-allow decision appears.
    /// </summary>
    public async Task<AuthorizeDecision> AuthorizeAsync(HookContext<TEntity> ctx, AuthorizeRequest req)
    {
        foreach (var h in _auth)
        {
            var d = await h.OnAuthorizeAsync(ctx, req);
            if (d is AuthorizeDecision.Forbid or AuthorizeDecision.Challenge) return d;
        }
        return AuthorizeDecision.Allowed();
    }

    /// <summary>
    /// Allow hooks to mutate query options; returns false if short-circuited.
    /// </summary>
    public async Task<bool> BuildOptionsAsync(HookContext<TEntity> ctx, QueryOptions opts)
    {
        foreach (var h in _opts)
        {
            await h.OnBuildingOptionsAsync(ctx, opts);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforeCollectionAsync(HookContext<TEntity> ctx, QueryOptions opts)
    {
        foreach (var h in _col)
        {
            await h.OnBeforeFetchAsync(ctx, opts);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterCollectionAsync(HookContext<TEntity> ctx, List<TEntity> items)
    {
        foreach (var h in _col)
        {
            await h.OnAfterFetchAsync(ctx, items);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforeModelFetchAsync(HookContext<TEntity> ctx, string id)
    {
        foreach (var h in _model)
        {
            await h.OnBeforeFetchAsync(ctx, id);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterModelFetchAsync(HookContext<TEntity> ctx, TEntity? model)
    {
        foreach (var h in _model)
        {
            await h.OnAfterFetchAsync(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforeSaveAsync(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in _model)
        {
            await h.OnBeforeSaveAsync(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterSaveAsync(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in _model)
        {
            await h.OnAfterSaveAsync(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforeDeleteAsync(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in _model)
        {
            await h.OnBeforeDeleteAsync(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterDeleteAsync(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in _model)
        {
            await h.OnAfterDeleteAsync(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> BeforePatchAsync(HookContext<TEntity> ctx, string id, object patch)
    {
        foreach (var h in _model)
        {
            await h.OnBeforePatchAsync(ctx, id, patch);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    public async Task<bool> AfterPatchAsync(HookContext<TEntity> ctx, TEntity model)
    {
        foreach (var h in _model)
        {
            await h.OnAfterPatchAsync(ctx, model);
            if (ctx.IsShortCircuited) return false;
        }
        return true;
    }

    /// <summary>
    /// Run emit hooks for collections; allow replacement of payload or short-circuit.
    /// </summary>
    public async Task<(bool replaced, object payload)> EmitCollectionAsync(HookContext<TEntity> ctx, object payload)
    {
        foreach (var h in _emit)
        {
            var d = await h.OnEmitCollectionAsync(ctx, payload);
            if (d is EmitDecision.Replace rep) return (true, rep.Payload);
            if (ctx.IsShortCircuited) return (true, ctx.ShortCircuitResult!);
        }
        return (false, payload);
    }

    /// <summary>
    /// Run emit hooks for single models; allow replacement of payload or short-circuit.
    /// </summary>
    public async Task<(bool replaced, object payload)> EmitModelAsync(HookContext<TEntity> ctx, object payload)
    {
        foreach (var h in _emit)
        {
            var d = await h.OnEmitModelAsync(ctx, payload);
            if (d is EmitDecision.Replace rep) return (true, rep.Payload);
            if (ctx.IsShortCircuited) return (true, ctx.ShortCircuitResult!);
        }
        return (false, payload);
    }
}
