namespace Koan.Web.Hooks;

/// <summary>
/// Authorization hook for allow/forbid/challenge decisions.
/// </summary>
public interface IAuthorizeHook<TEntity> : IOrderedHook
{
    Task<AuthorizeDecision> OnAuthorizeAsync(HookContext<TEntity> ctx, AuthorizeRequest req);
}