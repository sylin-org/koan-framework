using System.Collections.Generic;

namespace Sora.Orchestration;

public sealed record Plan(
    Profile Profile,
    IReadOnlyList<ServiceSpec> Services
);
