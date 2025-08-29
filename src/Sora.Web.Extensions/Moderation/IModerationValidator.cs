namespace Sora.Web.Extensions.Moderation;

public interface IModerationValidator<TEntity>
{
    public virtual ValueTask<FlowResult> ValidateSubmit(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> ValidateWithdraw(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> ValidateApprove(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> ValidateReject(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> ValidateReturn(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
}