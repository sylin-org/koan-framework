using System;
using Microsoft.Extensions.DependencyInjection;

namespace S8.Flow.Shared.Commands;

public sealed class FlowCommandContext
{
    public IServiceProvider Services { get; init; } = default!;
    public string? TargetAdapter { get; init; }
    public string? Issuer { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
}
