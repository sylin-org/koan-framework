using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Auth.Roles.Extensions;
using Koan.Web.Auth.Roles.Options;
using Koan.Web.Extensions;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Auth.Roles.Initialization;

public sealed class AuthRolesModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanWebAuthRoles();

        // Ensure MVC discovers controllers from this assembly
        services.AddKoanControllersFrom<Controllers.RolesAdminController>();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        module.AddTool(
            "Role Administration",
            $"/{Koan.Web.Auth.Roles.Infrastructure.AuthRoutes.Base}",
            "Manage role, alias, and policy bindings",
            capability: "auth.roles.admin");
    }
}

