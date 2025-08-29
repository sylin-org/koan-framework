using System.Threading;
using Microsoft.AspNetCore.Http;
using Sora.Web.Contracts;
using Sora.Web.Infrastructure;

namespace Sora.Web.Extensions.Moderation;

/// <summary>
/// Standard moderation flow with safe defaults.
/// - Validators: permissive by default; light guardrails for obvious bad inputs.
/// - Hooks: no-op; intended for apps to override in a custom flow when needed.
/// </summary>
public sealed class StandardModerationFlow<TEntity> : IModerationFlow<TEntity>, IModerationValidator<TEntity>
{
	// Validator defaults — permissive, with minimal guards that don't require I/O
	public ValueTask<FlowResult> ValidateSubmit(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.FromResult(FlowResult.Success);

	public ValueTask<FlowResult> ValidateWithdraw(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.FromResult(FlowResult.Success);

	public ValueTask<FlowResult> ValidateApprove(TransitionContext<TEntity> ctx, CancellationToken ct)
	{
		// If we clearly don't have a submitted snapshot, surface a 404. Base would otherwise no-op.
		return ValueTask.FromResult(ctx.SubmittedSnapshot is null
			? FlowResult.Fail(StatusCodes.Status404NotFound, SoraWebConstants.Codes.Moderation.NotFound, "Submitted snapshot not found for approval.")
			: FlowResult.Success);
	}

	public ValueTask<FlowResult> ValidateReject(TransitionContext<TEntity> ctx, CancellationToken ct)
	{
		if (ctx.SubmittedSnapshot is null)
			return ValueTask.FromResult(FlowResult.Fail(StatusCodes.Status404NotFound, SoraWebConstants.Codes.Moderation.NotFound, "Submitted snapshot not found for rejection."));
		if (ctx.Options is RejectOptions ro && string.IsNullOrWhiteSpace(ro.Reason))
			return ValueTask.FromResult(FlowResult.Fail(StatusCodes.Status400BadRequest, SoraWebConstants.Codes.Moderation.ReasonRequired, "A reason is required to reject."));
		return ValueTask.FromResult(FlowResult.Success);
	}

	public ValueTask<FlowResult> ValidateReturn(TransitionContext<TEntity> ctx, CancellationToken ct)
	{
		if (ctx.SubmittedSnapshot is null)
			return ValueTask.FromResult(FlowResult.Fail(StatusCodes.Status404NotFound, SoraWebConstants.Codes.Moderation.NotFound, "Submitted snapshot not found for return."));
		if (ctx.Options is RejectOptions ro && string.IsNullOrWhiteSpace(ro.Reason))
			return ValueTask.FromResult(FlowResult.Fail(StatusCodes.Status400BadRequest, SoraWebConstants.Codes.Moderation.ReasonRequired, "A reason is required to return to draft."));
		return ValueTask.FromResult(FlowResult.Success);
	}

	// Before* hooks — pure and fast by convention; default no-ops
	public ValueTask<FlowResult> BeforeSubmit(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.FromResult(FlowResult.Success);

	public ValueTask<FlowResult> BeforeWithdraw(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.FromResult(FlowResult.Success);

	public ValueTask<FlowResult> BeforeApprove(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.FromResult(FlowResult.Success);

	public ValueTask<FlowResult> BeforeReject(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.FromResult(FlowResult.Success);

	public ValueTask<FlowResult> BeforeReturn(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.FromResult(FlowResult.Success);

	// After* hooks — post-commit side effects; default no-ops
	public ValueTask AfterSubmitted(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.CompletedTask;

	public ValueTask AfterWithdrawn(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.CompletedTask;

	public ValueTask AfterApproved(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.CompletedTask;

	public ValueTask AfterRejected(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.CompletedTask;

	public ValueTask AfterReturned(TransitionContext<TEntity> ctx, CancellationToken ct)
		=> ValueTask.CompletedTask;
}
