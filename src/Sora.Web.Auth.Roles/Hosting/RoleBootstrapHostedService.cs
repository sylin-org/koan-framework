using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.Roles.Contracts;
using Sora.Web.Auth.Roles.Options;

namespace Sora.Web.Auth.Roles.Hosting;

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
            var existingRoles = await roles.All(cancellationToken).ConfigureAwait(false);
            var existingAliases = await aliases.All(cancellationToken).ConfigureAwait(false);
            var existingBindings = await bindings.All(cancellationToken).ConfigureAwait(false);
            if (existingRoles.Count == 0 && existingAliases.Count == 0 && existingBindings.Count == 0)
            {
                if (!EnvironmentIsProduction() || opt.AllowSeedingInProduction)
                {
                    if (opt.Roles.Count > 0)
                        await roles.UpsertMany(opt.Roles.Select(r => new SeedRoleDto(r.Id, r.Display, r.Description)), cancellationToken).ConfigureAwait(false);
                    if (opt.Aliases.Map.Count > 0)
                        await aliases.UpsertMany(opt.Aliases.Map.Select(kv => new SeedAliasDto(kv.Key, kv.Value)), cancellationToken).ConfigureAwait(false);
                    if (opt.PolicyBindings.Count > 0)
                        await bindings.UpsertMany(opt.PolicyBindings.Select(p => new SeedBindingDto(p.Id, p.Requirement)), cancellationToken).ConfigureAwait(false);

                    await snapshot.ReloadAsync(cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("Sora.Web.Auth.Roles: initial seed applied from configuration template.");
                }
                else
                {
                    logger.LogWarning("Sora.Web.Auth.Roles: initial seed skipped in Production. Enable AllowSeedingInProduction to allow.");
                }
            }

            // 2) Admin bootstrap gates (FirstUser/ClaimMatch) are enforced at attribution time; nothing to do here.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sora.Web.Auth.Roles: bootstrap hosted service failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool EnvironmentIsProduction()
        => string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);

    // Lightweight DTOs to pass into stores without leaking defaults
    private sealed record SeedRoleDto(string Id, string? Display, string? Description) : ISoraAuthRole
    {
        public byte[]? RowVersion => null;
    }
    private sealed record SeedAliasDto(string Id, string TargetRole) : ISoraAuthRoleAlias
    {
        public byte[]? RowVersion => null;
    }
    private sealed record SeedBindingDto(string Id, string Requirement) : ISoraAuthRolePolicyBinding
    {
        public byte[]? RowVersion => null;
    }
}
