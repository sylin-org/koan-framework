using Koan.Cache.Coherence.Messaging.Extensions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Coherence.Messaging.Initialization;

/// <summary>
/// Reference = Intent: referencing <c>Koan.Cache.Coherence.Messaging</c> auto-registers the
/// messaging-backed coherence channel. When the app already wires <c>Koan.Messaging</c>
/// (RabbitMQ, in-memory, etc.), no separate Redis pub/sub is needed for cache invalidation.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Coherence.Messaging";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCacheMessagingCoherence();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddSetting(
            "Transport",
            "koan-messaging",
            source: BootSettingSource.Auto,
            consumers: new[] { "Koan.Cache.Coherence.CoordinatorChannel" });
        module.AddSetting(
            "Priority",
            "150 (preempts redis-pubsub when both registered)",
            source: BootSettingSource.Auto);
    }
}
