using System;
using System.Collections.Generic;
using Koan.Core.Observability.Health;

namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminHealthDocument(
    HealthStatus Overall,
    IReadOnlyList<KoanAdminHealthComponent> Components,
    DateTimeOffset ComputedAtUtc
)
{
    public static KoanAdminHealthDocument Empty { get; } = new(
        HealthStatus.Unknown,
        [],
        DateTimeOffset.MinValue
    );
}

public sealed record KoanAdminHealthComponent(
    string Component,
    HealthStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc,
    IReadOnlyDictionary<string, string> Facts
);
