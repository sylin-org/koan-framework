using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Sora.Flow.Attributes;

namespace Sora.Flow.Core.Orchestration;

/// <summary>
/// Default Flow orchestrator that handles Flow entity intake when no custom orchestrator is defined.
/// Automatically registered by the framework when no user-defined [FlowOrchestrator] is found.
/// </summary>
internal class DefaultFlowOrchestrator : FlowOrchestratorBase
{
    public DefaultFlowOrchestrator(ILogger<DefaultFlowOrchestrator> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        : base(logger, configuration, serviceProvider)
    {
    }

    // Inherits all processing logic from base class
    // Users can override this by creating their own [FlowOrchestrator] class
}