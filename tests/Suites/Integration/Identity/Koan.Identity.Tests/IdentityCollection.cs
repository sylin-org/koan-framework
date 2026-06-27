using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// Shares ONE offline host across the identity specs (run serially). Essential: the entity statics resolve through
/// the process-static <c>AppHost.Current</c> (bound if-null at the first start) and the audit lifecycle hooks
/// register on a process-static registry — a second host would neither rebind nor re-register, so every fact must
/// ride a single shared host.
/// </summary>
[CollectionDefinition("identity")]
public sealed class IdentityCollection : ICollectionFixture<IdentityHostFixture>;
