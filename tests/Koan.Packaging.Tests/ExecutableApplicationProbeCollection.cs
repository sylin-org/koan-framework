using Xunit;

namespace Koan.Packaging.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExecutableApplicationProbeCollection
{
    public const string Name = "Executable application probes";
}
