using System.Security.Claims;

namespace Sora.Web.Extensions.Moderation;

public sealed class TransitionContext<TEntity>
{
    public required object Id { get; init; }
    public TEntity? Current { get; init; }
    public TEntity? SubmittedSnapshot { get; set; }
    public ClaimsPrincipal? User { get; init; }
    public IServiceProvider Services { get; init; } = default!;
    public object? Options { get; init; }
}