using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// Shares one offline store across the Identity specs so durable reconciliation and audit facts remain visible
/// between test classes. Module startup scopes Entity statics to the provider supplied for that invocation; the
/// shared fixture is a data-lifetime choice, not ambient-host ownership machinery.
/// </summary>
[CollectionDefinition("identity")]
public sealed class IdentityCollection : ICollectionFixture<IdentityHostFixture>;
