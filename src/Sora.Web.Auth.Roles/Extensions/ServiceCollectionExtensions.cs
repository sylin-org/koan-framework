using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core.Modules;
using Sora.Web.Auth.Roles.Contracts;
using Sora.Web.Auth.Roles.Infrastructure;
using Sora.Web.Auth.Roles.Options;
using Sora.Web.Auth.Roles.Services.Stores;

namespace Sora.Web.Auth.Roles.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraWebAuthRoles(this IServiceCollection services)
    {
        services.AddSoraOptions<RoleAttributionOptions>(RoleAttributionOptions.SectionPath);

    services.TryAddSingleton<IRoleAttributionService, Sora.Web.Auth.Roles.Services.DefaultRoleAttributionService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClaimsTransformation, SoraRoleClaimsTransformation>());
    services.TryAddEnumerable(ServiceDescriptor.Transient<IRoleMapContributor, Sora.Web.Auth.Roles.Contributors.DefaultCapabilityContributor>());

    // lightweight in-memory cache for attribution; apps can replace
    services.TryAddSingleton<IRoleAttributionCache, InMemoryRoleAttributionCache>();

    // live snapshot of DB-backed aliases and policy bindings
    services.TryAddSingleton<IRoleConfigSnapshotProvider, Sora.Web.Auth.Roles.Services.DefaultRoleConfigSnapshotProvider>();

    // Default stores behind first-class models; apps can replace via DI
    services.TryAddSingleton<IRoleStore, DefaultRoleStore>();
    services.TryAddSingleton<IRoleAliasStore, DefaultRoleAliasStore>();
    services.TryAddSingleton<IRolePolicyBindingStore, DefaultRolePolicyBindingStore>();
    services.TryAddSingleton<IRoleBootstrapStateStore, DefaultRoleBootstrapStateStore>();

    // Hosted seeder to initialize DB from options template and handle admin bootstrap gates
    services.AddHostedService<Sora.Web.Auth.Roles.Hosting.RoleBootstrapHostedService>();

        return services;
    }
}
