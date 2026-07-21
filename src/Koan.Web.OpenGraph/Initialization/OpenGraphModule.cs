using System;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Web.OpenGraph.Infrastructure;
using Koan.Web.OpenGraph.Hosting;
using Koan.Web.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.OpenGraph.Initialization;

/// <summary>
/// Self-registration for the OpenGraph pillar (Reference = Intent). Registers options and the
/// renderer and contributes middleware through Koan's canonical web pipeline. With no configured
/// shell or matching card, the pillar is inert.
/// </summary>
/// <remarks>
/// Initialization ordering (CORE-0091): this pillar is a web-layer module that composes on top of
/// Koan.Web, so it is ordered <c>[After]</c> the Koan.Web core module, matching the convention the
/// other web pillars follow (for example Koan.Web.Auth). The DI registrations below are themselves
/// order-independent (options binding plus two self-contained singletons), so this attribute declares
/// layering intent and keeps the boot report ordered web-core-then-pillar; it also future-proofs the
/// ordering should this pillar ever contribute to the request pipeline. The ordering is a soft
/// constraint: if Koan.Web is not loaded the target is simply ignored.
/// </remarks>
[After(typeof(Koan.Web.Initialization.WebModule))]
public sealed class OpenGraphModule : KoanModule
{
    private SocialCardRegistry? _cards;

    public override void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddKoanOptions<OpenGraphOptions>(Constants.Configuration.Section);
        _cards = SocialCardRegistry.GetOrCreate(services);
        services.TryAddSingleton<ShellCache>();
        services.TryAddSingleton<IOpenGraphCardRenderer, OpenGraphCardRenderer>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IKoanWebPipelineContributor, OpenGraphPipelineContributor>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configuration);

        module.Describe(Version);

        var section = configuration.GetSection(Constants.Configuration.Section);
        var options = new OpenGraphOptions();
        section.Bind(options);

        module.AddSetting(
            "enabled",
            options.Enabled.ToString(),
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.Enabled}");

        module.AddSetting(
            "shell-path",
            string.IsNullOrWhiteSpace(options.ShellPath) ? "(unset)" : options.ShellPath,
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.ShellPath}",
            state: string.IsNullOrWhiteSpace(options.ShellPath) ? ProvenanceSettingState.Default : ProvenanceSettingState.Configured);

        module.AddNote($"{_cards?.Registrations.Count ?? 0} host-owned social-card declaration(s).");
    }

}
