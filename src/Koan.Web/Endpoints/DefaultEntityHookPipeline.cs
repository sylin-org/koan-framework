using Koan.Data.Abstractions;
using Koan.Web.Hooks;

namespace Koan.Web.Endpoints;

internal sealed class DefaultEntityHookPipeline<TEntity> : IEntityHookPipeline<TEntity>
{
    private readonly HookRunner<TEntity> _runner;

    public DefaultEntityHookPipeline(
        IEnumerable<IAuthorizeHook<TEntity>> authorizeHooks,
        IEnumerable<IRequestOptionsHook<TEntity>> optionHooks,
        IEnumerable<ICollectionHook<TEntity>> collectionHooks,
        IEnumerable<IModelHook<TEntity>> modelHooks,
        IEnumerable<IEmitHook<TEntity>> emitHooks)
    {
        _runner = new HookRunner<TEntity>(authorizeHooks, optionHooks, collectionHooks, modelHooks, emitHooks);
    }

    public HookContext<TEntity> CreateContext(EntityRequestContext requestContext)
        => new HookContext<TEntity>(requestContext);

    public Task<AuthorizeDecision> AuthorizeAsync(HookContext<TEntity> context, AuthorizeRequest request)
        => _runner.AuthorizeAsync(context, request);

    public Task<bool> BuildOptionsAsync(HookContext<TEntity> context, QueryOptions options)
        => _runner.BuildOptionsAsync(context, options);

    public Task<bool> BeforeCollectionAsync(HookContext<TEntity> context, QueryOptions options)
        => _runner.BeforeCollectionAsync(context, options);

    public Task<bool> AfterCollectionAsync(HookContext<TEntity> context, List<TEntity> items)
        => _runner.AfterCollectionAsync(context, items);

    public Task<bool> BeforeModelFetchAsync(HookContext<TEntity> context, string id)
        => _runner.BeforeModelFetchAsync(context, id);

    public Task<bool> AfterModelFetchAsync(HookContext<TEntity> context, TEntity? model)
        => _runner.AfterModelFetchAsync(context, model);

    public Task<bool> BeforeSaveAsync(HookContext<TEntity> context, TEntity model)
        => _runner.BeforeSaveAsync(context, model);

    public Task<bool> AfterSaveAsync(HookContext<TEntity> context, TEntity model)
        => _runner.AfterSaveAsync(context, model);

    public Task<bool> BeforeDeleteAsync(HookContext<TEntity> context, TEntity model)
        => _runner.BeforeDeleteAsync(context, model);

    public Task<bool> AfterDeleteAsync(HookContext<TEntity> context, TEntity model)
        => _runner.AfterDeleteAsync(context, model);

    public Task<bool> BeforePatchAsync(HookContext<TEntity> context, string id, object patch)
        => _runner.BeforePatchAsync(context, id, patch);

    public Task<bool> AfterPatchAsync(HookContext<TEntity> context, TEntity model)
        => _runner.AfterPatchAsync(context, model);

    public Task<(bool replaced, object payload)> EmitCollectionAsync(HookContext<TEntity> context, object payload)
        => _runner.EmitCollectionAsync(context, payload);

    public Task<(bool replaced, object payload)> EmitModelAsync(HookContext<TEntity> context, object payload)
        => _runner.EmitModelAsync(context, payload);
}
