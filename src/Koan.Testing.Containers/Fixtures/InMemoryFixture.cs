using System.Threading.Tasks;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 dockerless fixture for the in-memory data adapter. No container, no temp artifacts — the
/// store lives in the booted host's process and is discarded when the per-test host disposes. Specs
/// isolate via per-test partitions. Mirrors the legacy <c>InMemoryConnectorFixture</c> config
/// (<c>memory://default</c>); <c>AddKoan()</c> wires the rest.
/// </summary>
public sealed class InMemoryFixture : KoanContainerFixture
{
    public override string Engine => "inmemory";
    protected override string Adapter => "inmemory";

    protected override Task<string> StartContainerAsync() => Task.FromResult("memory://default");

    protected override ValueTask StopContainerAsync() => ValueTask.CompletedTask;
}
