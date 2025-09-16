using System.Collections.Generic;

namespace Koan.Orchestration.Models;

public sealed record Plan(
    Profile Profile,
    IReadOnlyList<ServiceSpec> Services
);
