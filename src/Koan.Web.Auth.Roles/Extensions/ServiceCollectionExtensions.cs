using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core.Modules;
using Koan.Web.Auth.Roles.Contracts;
using Koan.Web.Auth.Roles.Options;
using Koan.Web.Auth.Roles.Services.Stores;

namespace Koan.Web.Auth.Roles.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanWebAuthRoles(this IServiceCollection services)
    {
        services.AddKoanOptions<RoleAttributionOptions>(RoleAttributionOptions.SectionPath);

        // Live snapshot of DB-backed aliases and policy bindings; surface used by the admin
        // controller and by host-application contributors that want to normalize roles via the
        // alias map.
        services.TryAddSingleton<IRoleConfigSnapshotProvider, Koan.Web.Auth.Roles.Services.DefaultRoleConfigSnapshotProvider>();

        // Default stores behind first-class models; apps can replace via DI.
        services.TryAddSingleton<IRoleStore, DefaultRoleStore>();
        services.TryAddSingleton<IRoleAliasStore, DefaultRoleAliasStore>();
        services.TryAddSingleton<IRolePolicyBindingStore, DefaultRolePolicyBindingStore>();
        services.TryAddSingleton<IRoleBootstrapStateStore, DefaultRoleBootstrapStateStore>();

        // Hosted seeder: initializes DB Role/RoleAlias/RolePolicyBinding entities from
        // configuration templates on first run. Admin-bootstrap modes moved to
        // AdminBootstrapContributor (see WEB-0065).
        services.AddHostedService<Koan.Web.Auth.Roles.Hosting.RoleBootstrapHostedService>();

        // AdminBootstrapContributor is auto-discovered by Koan.Web.Auth's contributor scan when
        // this assembly is loaded — no explicit DI registration here.

        return services;
    }
}
