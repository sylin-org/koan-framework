using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Roles.Contracts;
using Koan.Web.Auth.Roles.Infrastructure;
using Koan.Web.Auth.Roles.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Roles.Controllers;

[ApiController]
[Route(AuthRoutes.Base)]
[Authorize(Policy = RoleAdminPolicy.Name)]
public sealed class RolesAdminController : ControllerBase
{
    private readonly IRoleStore _roles;
    private readonly IRoleAliasStore _aliases;
    private readonly IRolePolicyBindingStore _bindings;
    private readonly IOptionsMonitor<RoleAttributionOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly IRoleAttributionCache _cache;
    private readonly IRoleConfigSnapshotProvider _snapshotProvider;

    public RolesAdminController(IRoleStore roles, IRoleAliasStore aliases, IRolePolicyBindingStore bindings, IOptionsMonitor<RoleAttributionOptions> options, IHostEnvironment env, IRoleAttributionCache cache, IRoleConfigSnapshotProvider snapshotProvider)
    { _roles = roles; _aliases = aliases; _bindings = bindings; _options = options; _env = env; _cache = cache; _snapshotProvider = snapshotProvider; }

    // Roles
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IKoanAuthRole>>> GetRoles(CancellationToken ct)
        => Ok(await _roles.All(ct));

    [HttpPut("{key}")]
    public async Task<IActionResult> PutRole([FromRoute] string key, [FromBody] RoleDto dto, CancellationToken ct)
    {
        var item = new RoleDto { Id = key, Display = dto.Display, Description = dto.Description, RowVersion = dto.RowVersion };
        await _roles.UpsertMany(new[] { item }, ct);
        return NoContent();
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> DeleteRole([FromRoute] string key, CancellationToken ct)
        => await _roles.Delete(key, ct) ? NoContent() : NotFound();

    // Aliases
    [HttpGet(AuthRoutes.Aliases)]
    public async Task<ActionResult<IReadOnlyList<IKoanAuthRoleAlias>>> GetAliases(CancellationToken ct)
        => Ok(await _aliases.All(ct));

    [HttpPut(AuthRoutes.Aliases + "/{alias}")]
    public async Task<IActionResult> PutAlias([FromRoute] string alias, [FromBody] RoleAliasDto dto, CancellationToken ct)
    {
        var item = new RoleAliasDto { Id = alias, TargetRole = dto.TargetRole, RowVersion = dto.RowVersion };
        await _aliases.UpsertMany(new[] { item }, ct);
        return NoContent();
    }

    [HttpDelete(AuthRoutes.Aliases + "/{alias}")]
    public async Task<IActionResult> DeleteAlias([FromRoute] string alias, CancellationToken ct)
        => await _aliases.Delete(alias, ct) ? NoContent() : NotFound();

    // Policy bindings
    [HttpGet(AuthRoutes.PolicyBindings)]
    public async Task<ActionResult<IReadOnlyList<IKoanAuthRolePolicyBinding>>> GetPolicyBindings(CancellationToken ct)
        => Ok(await _bindings.All(ct));

    [HttpPut(AuthRoutes.PolicyBindings + "/{policy}")]
    public async Task<IActionResult> PutPolicyBinding([FromRoute] string policy, [FromBody] RolePolicyBindingDto dto, CancellationToken ct)
    {
        var item = new RolePolicyBindingDto { Id = policy, Requirement = dto.Requirement, RowVersion = dto.RowVersion };
        await _bindings.UpsertMany(new[] { item }, ct);
        return NoContent();
    }

    [HttpDelete(AuthRoutes.PolicyBindings + "/{policy}")]
    public async Task<IActionResult> DeletePolicyBinding([FromRoute] string policy, CancellationToken ct)
        => await _bindings.Delete(policy, ct) ? NoContent() : NotFound();

    // Import/export/reload
    [HttpGet(AuthRoutes.Export)]
    public async Task<ActionResult<object>> Export(CancellationToken ct)
    {
        var roles = await _roles.All(ct);
        var bindings = await _bindings.All(ct);
        var opt = _options.CurrentValue;
        var payload = new
        {
            Koan = new
            {
                Web = new
                {
                    Auth = new
                    {
                        Roles = new
                        {
                            ClaimKeys = opt.ClaimKeys,
                            Aliases = opt.Aliases,
                            EmitPermissionClaims = opt.EmitPermissionClaims,
                            MaxRoles = opt.MaxRoles,
                            MaxPermissions = opt.MaxPermissions,
                            DevFallback = opt.DevFallback,
                            Roles = roles.Select(r => new RoleAttributionOptions.RoleSeed { Id = r.Id, Display = r.Display, Description = r.Description }).ToArray(),
                            PolicyBindings = bindings.Select(b => new RoleAttributionOptions.RolePolicyBindingSeed { Id = b.Id, Requirement = b.Requirement }).ToArray()
                        }
                    }
                }
            }
        };
        return Ok(payload);
    }

    [HttpPost(AuthRoutes.Import)]
    public async Task<ActionResult<object>> Import([FromQuery] bool? dryRun = null, [FromQuery] bool? force = null, CancellationToken ct = default)
    {
        var opt = _options.CurrentValue;
        bool isProd = string.Equals(_env.EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase);
        bool allowMagic = Koan.Core.KoanEnv.AllowMagicInProduction || opt.AllowSeedingInProduction;
        if (isProd && !allowMagic)
            return StatusCode(403, new { error = "Import is disabled in Production. Set KoanEnv.AllowMagicInProduction or Roles.AllowSeedingInProduction to enable." });

        // Build diff against template from options
    var desiredRoles = opt.Roles.Select(r => new RolesAdminController.RoleDto { Id = r.Id, Display = r.Display, Description = r.Description }).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    var desiredBindings = opt.PolicyBindings.Select(b => new RolesAdminController.RolePolicyBindingDto { Id = b.Id, Requirement = b.Requirement }).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    var desiredAliases = opt.Aliases.Map.ToDictionary(k => k.Key, v => new RolesAdminController.RoleAliasDto { Id = v.Key, TargetRole = v.Value }, StringComparer.OrdinalIgnoreCase);

    var currentRoles = (await _roles.All(ct)).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    var currentBindings = (await _bindings.All(ct)).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    var currentAliases = (await _aliases.All(ct)).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        var toAddOrUpdateRoles = new List<RoleDto>();
        var toDeleteRoles = new List<string>();
        foreach (var kvp in desiredRoles)
        {
            if (!currentRoles.TryGetValue(kvp.Key, out var existing) || !string.Equals(existing.Display, kvp.Value.Display, StringComparison.Ordinal) || !string.Equals(existing.Description, kvp.Value.Description, StringComparison.Ordinal))
                toAddOrUpdateRoles.Add(kvp.Value);
        }
        if (force == true)
        {
            foreach (var key in currentRoles.Keys)
                if (!desiredRoles.ContainsKey(key)) toDeleteRoles.Add(key);
        }

        var toAddOrUpdateBindings = new List<RolePolicyBindingDto>();
        var toDeleteBindings = new List<string>();
        foreach (var kvp in desiredBindings)
        {
            if (!currentBindings.TryGetValue(kvp.Key, out var existing) || !string.Equals(existing.Requirement, kvp.Value.Requirement, StringComparison.Ordinal))
                toAddOrUpdateBindings.Add(kvp.Value);
        }
        if (force == true)
        {
            foreach (var key in currentBindings.Keys)
                if (!desiredBindings.ContainsKey(key)) toDeleteBindings.Add(key);
        }

        var toAddOrUpdateAliases = new List<RoleAliasDto>();
        var toDeleteAliases = new List<string>();
        foreach (var kvp in desiredAliases)
        {
            if (!currentAliases.TryGetValue(kvp.Key, out var existing) || !string.Equals(existing.TargetRole, kvp.Value.TargetRole, StringComparison.Ordinal))
                toAddOrUpdateAliases.Add(kvp.Value);
        }
        if (force == true)
        {
            foreach (var key in currentAliases.Keys)
                if (!desiredAliases.ContainsKey(key)) toDeleteAliases.Add(key);
        }

        var diff = new
        {
            roles = new { upsert = toAddOrUpdateRoles.Select(r => r.Id).ToArray(), delete = toDeleteRoles.ToArray() },
            aliases = new { upsert = toAddOrUpdateAliases.Select(a => a.Id).ToArray(), delete = toDeleteAliases.ToArray() },
            policyBindings = new { upsert = toAddOrUpdateBindings.Select(b => b.Id).ToArray(), delete = toDeleteBindings.ToArray() }
        };

        if (dryRun == true)
            return Ok(new { applied = false, diff });

        if (toAddOrUpdateRoles.Count > 0)
            await _roles.UpsertMany(toAddOrUpdateRoles, ct);
        foreach (var k in toDeleteRoles)
            await _roles.Delete(k, ct);

        if (toAddOrUpdateAliases.Count > 0)
            await _aliases.UpsertMany(toAddOrUpdateAliases, ct);
        foreach (var k in toDeleteAliases)
            await _aliases.Delete(k, ct);

        if (toAddOrUpdateBindings.Count > 0)
            await _bindings.UpsertMany(toAddOrUpdateBindings, ct);
        foreach (var k in toDeleteBindings)
            await _bindings.Delete(k, ct);

    await _snapshotProvider.ReloadAsync(ct);
    _cache.Clear(); // invalidate attribution cache
        return Ok(new { applied = true, diff });
    }

    [HttpPost(AuthRoutes.Reload)]
    public async Task<IActionResult> Reload(CancellationToken ct)
    {
        await _snapshotProvider.ReloadAsync(ct);
        _cache.Clear();
        return NoContent();
    }

    // Minimal DTOs implementing the contracts so callers can post simple payloads
    public sealed class RoleDto : IKoanAuthRole
    {
        public string Id { get; set; } = string.Empty;
        public string? Display { get; set; }
        public string? Description { get; set; }
        public byte[]? RowVersion { get; set; }
    }
    public sealed class RoleAliasDto : IKoanAuthRoleAlias
    {
        public string Id { get; set; } = string.Empty;
        public string TargetRole { get; set; } = string.Empty;
        public byte[]? RowVersion { get; set; }
    }
    public sealed class RolePolicyBindingDto : IKoanAuthRolePolicyBinding
    {
        public string Id { get; set; } = string.Empty;
        public string Requirement { get; set; } = string.Empty;
        public byte[]? RowVersion { get; set; }
    }
}
