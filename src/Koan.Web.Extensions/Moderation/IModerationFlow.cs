using System.Threading;

namespace Koan.Web.Extensions.Moderation;

public interface IModerationFlow<TEntity>
{
    // Before* hooks: pre-commit, should be pure/fast. Default no-op.
    public virtual ValueTask<FlowResult> BeforeSubmit(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> BeforeWithdraw(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> BeforeApprove(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> BeforeReject(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> BeforeReturn(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);

    // After* hooks: post-commit, best-effort. Default no-op.
    public virtual ValueTask AfterSubmitted(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.CompletedTask;
    public virtual ValueTask AfterWithdrawn(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.CompletedTask;
    public virtual ValueTask AfterApproved(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.CompletedTask;
    public virtual ValueTask AfterRejected(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.CompletedTask;
    public virtual ValueTask AfterReturned(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.CompletedTask;
}