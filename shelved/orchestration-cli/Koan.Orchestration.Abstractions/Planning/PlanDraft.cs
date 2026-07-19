using System.Collections.Generic;

namespace Koan.Orchestration.Planning;

public sealed record PlanDraft(
    IReadOnlyList<ServiceRequirement> Services,
    bool IncludeApp,
    int AppHttpPort
);
