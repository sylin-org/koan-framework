namespace Koan.Web.Hooks;

/// <summary>
/// Emission hooks can transform or replace payloads before the response.
/// </summary>
public interface IEmitHook<TEntity> : IOrderedHook
{
    Task<EmitDecision> OnEmitCollection(HookContext<TEntity> ctx, object payload);
    Task<EmitDecision> OnEmitModel(HookContext<TEntity> ctx, object payload);
}