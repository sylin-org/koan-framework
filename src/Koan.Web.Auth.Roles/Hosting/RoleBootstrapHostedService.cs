using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Roles.Contracts;
using Koan.Web.Auth.Roles.Options;

namespace Koan.Web.Auth.Roles.Hosting;

internal sealed class RoleBootstrapHostedService(
    IRoleStore roles,
    IRoleAliasStore aliases,
    IRolePolicyBindingStore bindings,
    IRoleConfigSnapshotProvider snapshot,
    IOptionsMonitor<RoleAttributionOptions> options,
    ILogger<RoleBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var opt = options.CurrentValue;
            // 1) Initial seed when empty
            var existingRoles = await roles.All(cancellationToken);
            var existingAliases = await aliases.All(cancellationToken);
            var existingBindings = await bindings.All(cancellationToken);
            if (existingRoles.Count == 0 && existingAliases.Count == 0 && existingBindings.Count == 0)
            {
                if (!EnvironmentIsProduction() || opt.AllowSeedingInProduction)
                {
                    if (opt.Roles.Count > 0)
                        await roles.UpsertMany(opt.Roles.Select(r => new SeedRoleDto(r.Id, r.Display, r.Description)), cancellationToken);
                    if (opt.Aliases.Map.Count > 0)
                        await aliases.UpsertMany(opt.Aliases.Map.Select(kv => new SeedAliasDto(kv.Key, kv.Value)), cancellationToken);
                    if (opt.PolicyBindings.Count > 0)
                        await bindings.UpsertMany(opt.PolicyBindings.Select(p => new SeedBindingDto(p.Id, p.Requirement)), cancellationToken);

                    await snapshot.ReloadAsync(cancellationToken);
                    logger.LogInformation("Koan.Web.Auth.Roles: initial seed applied from configuration template.");
                }
                else
                {
                    logger.LogWarning("Koan.Web.Auth.Roles: initial seed skipped in Production. Enable AllowSeedingInProduction to allow.");
                }
            }

            // 2) Admin bootstrap gates (FirstUser/ClaimMatch) are enforced at attribution time; nothing to do here.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Koan.Web.Auth.Roles: bootstrap hosted service failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool EnvironmentIsProduction()
        => string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);

    // Lightweight DTOs to pass into stores without leaking defaults
    private sealed record SeedRoleDto(string Id, string? Display, string? Description) : IKoanAuthRole
    {
        public byte[]? RowVersion => null;
    }
    private sealed record SeedAliasDto(string Id, string TargetRole) : IKoanAuthRoleAlias
    {
        public byte[]? RowVersion => null;
    }
    private sealed record SeedBindingDto(string Id, string Requirement) : IKoanAuthRolePolicyBinding
    {
        public byte[]? RowVersion => null;
    }
}
