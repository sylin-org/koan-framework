using System;
using System.Collections.Generic;

namespace Koan.Orchestration.Models;

public sealed record ServiceSpec(
    string Id,
    string Image,
    IReadOnlyDictionary<string, string?> Env,
    IReadOnlyList<(int Host, int Container)> Ports,
    IReadOnlyList<(string Source, string Target, bool Named)> Volumes,
    HealthSpec? Health,
    ServiceType? Type,
    IReadOnlyList<string> DependsOn
);