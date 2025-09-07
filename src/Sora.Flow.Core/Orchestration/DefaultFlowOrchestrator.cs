using Microsoft.Extensions.Logging;
using Sora.Flow.Attributes;

namespace Sora.Flow.Core.Orchestration;

/// <summary>
/// Default Flow orchestrator that handles Flow entity intake when no custom orchestrator is defined.
/// Automatically registered by the framework when no user-defined [FlowOrchestrator] is found.
/// </summary>
[FlowOrchestrator]
internal class DefaultFlowOrchestrator : FlowOrchestratorBase
{
    public DefaultFlowOrchestrator(ILogger<DefaultFlowOrchestrator> logger, IServiceProvider serviceProvider)
        : base(logger, serviceProvider)
    {
    }

    // Inherits all processing logic from base class
    // Users can override this by creating their own [FlowOrchestrator] class
}