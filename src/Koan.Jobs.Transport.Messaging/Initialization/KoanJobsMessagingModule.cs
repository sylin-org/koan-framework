using Koan.Core;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Jobs.Transport.Messaging.Initialization;

/// <summary>
/// Reference = Intent (ARCH-0086): referencing <c>Koan.Jobs.Transport.Messaging</c> routes the Jobs push-dispatch
/// wake across nodes via Koan.Messaging — workers on every node claim newly-submitted work without waiting out
/// the poll interval.
/// </summary>
public sealed class KoanJobsMessagingModule : KoanModule
{
    public override string Id => "Koan.Jobs.Transport.Messaging";

    public override void Register(IServiceCollection services) => services.AddKoanJobsMessagingTransport();

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version).SetSetting("jobs.transport", b => b
            .Label("Push transport")
            .Value("koan-messaging (cross-node)"));
}
