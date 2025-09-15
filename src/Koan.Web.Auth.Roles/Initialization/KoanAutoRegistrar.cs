using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Auth.Roles.Extensions;
using Koan.Web.Auth.Roles.Options;

namespace Koan.Web.Auth.Roles.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Roles";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanWebAuthRoles();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var section = cfg.GetSection(RoleAttributionOptions.SectionPath);
        report.AddSetting("Auth:Roles:EmitPermissionClaims", section.GetValue("EmitPermissionClaims", true).ToString());
    }
}
