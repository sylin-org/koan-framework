using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.Roles.Contracts;
using Sora.Web.Auth.Roles.Options;

namespace Sora.Web.Auth.Roles.Infrastructure;

public sealed class SoraRoleClaimsTransformation : IClaimsTransformation
{
    private readonly IRoleAttributionService _service;
    private readonly IOptionsMonitor<RoleAttributionOptions> _options;
    private readonly ILogger<SoraRoleClaimsTransformation> _logger;

    public SoraRoleClaimsTransformation(IRoleAttributionService service, IOptionsMonitor<RoleAttributionOptions> options, ILogger<SoraRoleClaimsTransformation> logger)
    {
        _service = service;
        _options = options;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
    if (principal?.Identity?.IsAuthenticated != true) return principal ?? new ClaimsPrincipal();

        var currentStamp = principal.FindFirst(RoleClaimConstants.SoraRoleVersion)?.Value;
        var result = await _service.ComputeAsync(principal).ConfigureAwait(false);
        if (currentStamp == result.Stamp)
            return principal; // already enriched for this stamp

        var id = principal.Identity as ClaimsIdentity ?? new ClaimsIdentity();

        // Remove prior Sora-added claims to avoid duplicates (role claims may be merged; we only remove Sora-specific types)
        id.TryRemoveType(RoleClaimConstants.SoraRoleVersion);
        id.TryRemoveType(RoleClaimConstants.SoraPermission);

        // Add roles as standard role claims; Authorization uses ClaimTypes.Role
        foreach (var role in result.Roles)
            id.AddClaim(new Claim(RoleClaimConstants.RoleType, role));

        if (_options.CurrentValue.EmitPermissionClaims)
        {
            foreach (var perm in result.Permissions)
                id.AddClaim(new Claim(RoleClaimConstants.SoraPermission, perm));
        }

        if (!string.IsNullOrEmpty(result.Stamp))
            id.AddClaim(new Claim(RoleClaimConstants.SoraRoleVersion, result.Stamp));

        // If we created a new identity, attach it
        if (!ReferenceEquals(id, principal.Identity))
        {
            var identities = principal.Identities.ToList();
            identities.Add(id);
            return new ClaimsPrincipal(identities);
        }
    return principal ?? new ClaimsPrincipal();
    }
}

file static class ClaimsIdentityExtensions
{
    public static void TryRemoveType(this ClaimsIdentity id, string type)
    {
        var existing = id.FindAll(type).ToArray();
        if (existing.Length == 0) return;
        foreach (var c in existing)
            id.TryRemoveClaim(c);
    }
}
