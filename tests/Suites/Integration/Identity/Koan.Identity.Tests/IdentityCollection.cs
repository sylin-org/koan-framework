using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// Shares one offline store across the Identity specs so durable reconciliation and audit facts remain visible
/// between test classes. Collection serialization protects the shared store, while each spec enters the fixture
/// provider through <see cref="IdentityHostScopedSpec"/> so a newer or failed host cannot change later Entity
/// routing. The shared fixture is a data-lifetime choice, not ambient-host ownership machinery.
/// </summary>
[CollectionDefinition("identity")]
public sealed class IdentityCollection : ICollectionFixture<IdentityHostFixture>;
