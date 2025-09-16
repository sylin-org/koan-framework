using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core.Modules;
using Koan.Web.Auth.Roles.Contracts;
using Koan.Web.Auth.Roles.Infrastructure;
using Koan.Web.Auth.Roles.Options;
using Koan.Web.Auth.Roles.Services.Stores;

namespace Koan.Web.Auth.Roles.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanWebAuthRoles(this IServiceCollection services)
    {
        services.AddKoanOptions<RoleAttributionOptions>(RoleAttributionOptions.SectionPath);

    services.TryAddSingleton<IRoleAttributionService, Koan.Web.Auth.Roles.Services.DefaultRoleAttributionService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClaimsTransformation, KoanRoleClaimsTransformation>());
    services.TryAddEnumerable(ServiceDescriptor.Transient<IRoleMapContributor, Koan.Web.Auth.Roles.Contributors.DefaultCapabilityContributor>());

    // lightweight in-memory cache for attribution; apps can replace
    services.TryAddSingleton<IRoleAttributionCache, InMemoryRoleAttributionCache>();

    // live snapshot of DB-backed aliases and policy bindings
    services.TryAddSingleton<IRoleConfigSnapshotProvider, Koan.Web.Auth.Roles.Services.DefaultRoleConfigSnapshotProvider>();

    // Default stores behind first-class models; apps can replace via DI
    services.TryAddSingleton<IRoleStore, DefaultRoleStore>();
    services.TryAddSingleton<IRoleAliasStore, DefaultRoleAliasStore>();
    services.TryAddSingleton<IRolePolicyBindingStore, DefaultRolePolicyBindingStore>();
    services.TryAddSingleton<IRoleBootstrapStateStore, DefaultRoleBootstrapStateStore>();

    // Hosted seeder to initialize DB from options template and handle admin bootstrap gates
    services.AddHostedService<Koan.Web.Auth.Roles.Hosting.RoleBootstrapHostedService>();

        return services;
    }
}
