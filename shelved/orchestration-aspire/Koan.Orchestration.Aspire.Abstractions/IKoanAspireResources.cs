using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Orchestration.Aspire;

/// <summary>Contributes Aspire resources without activating Koan's functional Aspire runtime.</summary>
public interface IKoanAspireResources
{
    /// <summary>Registers the resources required by this referenced capability.</summary>
    void RegisterAspireResources(
        IDistributedApplicationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment);

    /// <summary>Gets the registration priority; infrastructure resources should register before applications.</summary>
    int Priority => 1000;

    /// <summary>Determines whether this contribution participates in the current AppHost.</summary>
    bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment) => true;
}
