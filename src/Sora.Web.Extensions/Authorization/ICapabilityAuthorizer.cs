using System.Security.Claims;

namespace Sora.Web.Extensions.Authorization;

public interface ICapabilityAuthorizer
{
    bool IsAllowed(ClaimsPrincipal user, Type entityType, string capabilityAction);
}