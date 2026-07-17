using Koan.Testing;
using TaskGraph;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Koan.Samples.TaskGraph.Tests;

/// <summary>
/// The sample's real SQLite User entity inherits Koan's provider conformance battery. Cache and
/// embedding cells self-skip because this entity deliberately declares neither capability.
/// </summary>
public sealed class UserConformance : EntityConformanceSpecs<User>
{
    protected override User NewValid() => new() { Name = "Ada Lovelace", Email = "ada@example.com" };
}
