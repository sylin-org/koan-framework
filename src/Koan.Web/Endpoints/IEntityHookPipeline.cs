using Koan.Data.Abstractions;
using Koan.Web.Hooks;

namespace Koan.Web.Endpoints;

/// <summary>
/// Abstraction for orchestrating entity hooks outside of HTTP controllers.
/// Allows alternative protocols to plug in custom sequencing or diagnostics.
/// </summary>
public interface IEntityHookPipeline<TEntity>
{
    HookContext<TEntity> CreateContext(EntityRequestContext requestContext);

    Task<AuthorizeDecision> AuthorizeAsync(HookContext<TEntity> context, AuthorizeRequest request);

    Task<bool> BuildOptionsAsync(HookContext<TEntity> context, QueryOptions options);

    Task<bool> BeforeCollectionAsync(HookContext<TEntity> context, QueryOptions options);

    Task<bool> AfterCollectionAsync(HookContext<TEntity> context, List<TEntity> items);

    Task<bool> BeforeModelFetchAsync(HookContext<TEntity> context, string id);

    Task<bool> AfterModelFetchAsync(HookContext<TEntity> context, TEntity? model);

    Task<bool> BeforeSaveAsync(HookContext<TEntity> context, TEntity model);

    Task<bool> AfterSaveAsync(HookContext<TEntity> context, TEntity model);

    Task<bool> BeforeDeleteAsync(HookContext<TEntity> context, TEntity model);

    Task<bool> AfterDeleteAsync(HookContext<TEntity> context, TEntity model);

    Task<bool> BeforePatchAsync(HookContext<TEntity> context, string id, object patch);

    Task<bool> AfterPatchAsync(HookContext<TEntity> context, TEntity model);

    Task<(bool replaced, object payload)> EmitCollectionAsync(HookContext<TEntity> context, object payload);

    Task<(bool replaced, object payload)> EmitModelAsync(HookContext<TEntity> context, object payload);
}
