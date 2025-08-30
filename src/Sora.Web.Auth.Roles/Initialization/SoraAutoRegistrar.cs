using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Web.Auth.Roles.Extensions;
using Sora.Web.Auth.Roles.Options;

namespace Sora.Web.Auth.Roles.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Auth.Roles";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSoraWebAuthRoles();
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var section = cfg.GetSection(RoleAttributionOptions.SectionPath);
        report.AddSetting("Auth:Roles:EmitPermissionClaims", section.GetValue("EmitPermissionClaims", true).ToString());
    }
}
