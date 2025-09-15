using System.Security.Claims;

namespace Koan.Web.Extensions.Authorization;

public interface ICapabilityAuthorizer
{
    bool IsAllowed(ClaimsPrincipal user, Type entityType, string capabilityAction);
}