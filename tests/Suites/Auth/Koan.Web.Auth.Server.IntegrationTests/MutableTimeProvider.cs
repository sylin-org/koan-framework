using System;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>A TimeProvider whose "now" can be advanced — for deterministic key-rotation tests.</summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
