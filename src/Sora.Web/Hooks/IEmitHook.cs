namespace Sora.Web.Hooks;

/// <summary>
/// Emission hooks can transform or replace payloads before the response.
/// </summary>
public interface IEmitHook<TEntity> : IOrderedHook
{
    Task<EmitDecision> OnEmitCollectionAsync(HookContext<TEntity> ctx, object payload);
    Task<EmitDecision> OnEmitModelAsync(HookContext<TEntity> ctx, object payload);
}