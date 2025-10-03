using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Koan.Canon.Attributes;

namespace Koan.Canon.Core.Orchestration;

/// <summary>
/// Default Canon orchestrator that handles Canon entity intake when no custom orchestrator is defined.
/// Automatically registered by the framework when no user-defined [CanonOrchestrator] is found.
/// </summary>
internal class DefaultCanonOrchestrator : CanonOrchestratorBase
{
    public DefaultCanonOrchestrator(ILogger<DefaultCanonOrchestrator> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        : base(logger, configuration, serviceProvider)
    {
    }

    // Inherits all processing logic from base class
    // Users can override this by creating their own [CanonOrchestrator] class
}

