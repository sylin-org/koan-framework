using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Auth.Roles.Extensions;
using Koan.Web.Auth.Roles.Options;
using Koan.Web.Extensions;

namespace Koan.Web.Auth.Roles.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Roles";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanWebAuthRoles();

        // Ensure MVC discovers controllers from this assembly
        services.AddKoanControllersFrom<Controllers.RolesAdminController>();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var emitPermissionClaims = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{RoleAttributionOptions.SectionPath}:EmitPermissionClaims",
            true);

        report.AddSetting(
            "Auth:Roles:EmitPermissionClaims",
            emitPermissionClaims.Value.ToString(),
            source: emitPermissionClaims.Source,
            consumers: new[] { "Koan.Web.Auth.Roles.PermissionEmitter" });

        report.AddTool(
            "Role Administration",
            $"/{Koan.Web.Auth.Roles.Infrastructure.AuthRoutes.Base}",
            "Manage role, alias, and policy bindings",
            capability: "auth.roles.admin");
    }
}
