using System.Collections.Generic;

namespace Sora.Orchestration.Planning;

public sealed record PlanDraft(
    IReadOnlyList<ServiceRequirement> Services,
    bool IncludeApp,
    int AppHttpPort
);
