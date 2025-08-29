using System.Security.Claims;
using System.Threading;

namespace Sora.Web.Extensions.Moderation;

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

public interface IModerationValidator<TEntity>
{
    public virtual ValueTask<FlowResult> ValidateSubmit(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> ValidateWithdraw(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> ValidateApprove(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> ValidateReject(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
    public virtual ValueTask<FlowResult> ValidateReturn(TransitionContext<TEntity> ctx, CancellationToken ct) => ValueTask.FromResult(FlowResult.Success);
}

public readonly record struct FlowResult(bool Ok, int? Status, string? Code, string? Message)
{
    public static FlowResult Success => new(true, null, null, null);
    public static FlowResult Fail(int status, string code, string message) => new(false, status, code, message);
}

public sealed class TransitionContext<TEntity>
{
    public required object Id { get; init; }
    public TEntity? Current { get; init; }
    public TEntity? SubmittedSnapshot { get; set; }
    public ClaimsPrincipal? User { get; init; }
    public IServiceProvider Services { get; init; } = default!;
    public object? Options { get; init; }
}
