using System.Collections.Generic;

namespace Sora.Orchestration.Models;

public sealed record Plan(
    Profile Profile,
    IReadOnlyList<ServiceSpec> Services
);
