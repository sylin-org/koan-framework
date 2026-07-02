using Microsoft.AspNetCore.Authorization;

namespace Koan.Tenancy.Web.Authorization;

/// <summary>The requirement behind <see cref="TenancyWebPolicies.Operator"/> — carries no data; the posture-aware
/// decision lives in <see cref="OperatorAuthorizationHandler"/> (ARCH-0104).</summary>
public sealed class OperatorRequirement : IAuthorizationRequirement
{
}
