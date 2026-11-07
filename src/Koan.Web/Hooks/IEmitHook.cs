namespace Koan.Web.Hooks;

/// <summary>
/// Emission hooks can transform or replace payloads before the response. Each passes the payload on unless a hook says otherwise.
/// </summary>
public interface IEmitHook<TEntity> : IOrderedHook
{
    Task<EmitDecision> OnEmitCollection(HookContext<TEntity> ctx, object payload) => Task.FromResult<EmitDecision>(EmitDecision.Next());
    Task<EmitDecision> OnEmitModel(HookContext<TEntity> ctx, object payload) => Task.FromResult<EmitDecision>(EmitDecision.Next());
}
