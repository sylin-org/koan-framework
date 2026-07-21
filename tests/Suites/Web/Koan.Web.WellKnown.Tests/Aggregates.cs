using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Web.WellKnown.Tests;

/// <summary>A normal aggregate that resolves to the in-memory adapter and self-reports capabilities fine.</summary>
public sealed class HealthyAggregate : Entity<HealthyAggregate>
{
    public string Name { get; set; } = "";
}

/// <summary>
/// An aggregate pinned to a NON-EXISTENT adapter. <c>WellKnownController.Aggregates</c> resolves a repository
/// per aggregate via <c>GetRepository&lt;FaultyAggregate, string&gt;()</c>, which throws
/// <see cref="System.InvalidOperationException"/> ("No data adapter factory for provider 'does-not-exist'") —
/// the exact per-aggregate self-report fault the F2-web degradable catch must absorb without failing the endpoint.
/// </summary>
[DataAdapter("does-not-exist")]
public sealed class FaultyAggregate : Entity<FaultyAggregate>
{
    public string Name { get; set; } = "";
}
