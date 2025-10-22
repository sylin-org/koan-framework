using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;

namespace Koan.Web.Extensions.Authorization.Internal;

internal sealed class DelegateClaimsTransformation(Func<ClaimsPrincipal, Task<ClaimsPrincipal>> transformer) : IClaimsTransformation
{
    private readonly Func<ClaimsPrincipal, Task<ClaimsPrincipal>> _transformer = transformer;

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        => _transformer(principal);
}
