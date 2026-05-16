using Xunit;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// All E2E tests share AppHost.Current (a global static) and must run sequentially.
/// This collection definition prevents xUnit from running test classes in parallel.
/// </summary>
[CollectionDefinition("EndToEnd", DisableParallelization = true)]
public sealed class EndToEndCollection;
