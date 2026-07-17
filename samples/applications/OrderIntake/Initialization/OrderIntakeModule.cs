using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace OrderIntake.Initialization;

/// <summary>Explains the application-owned workload contract at startup.</summary>
public sealed class OrderIntakeModule : KoanModule
{
    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version, "Bounded order intake with verified, durable workload receipts.");
        module.AddNote("Local runs without infrastructure; optional sources fail with their exact compose correction.");
    }
}
