using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.InMemory.Tests;

public sealed class InMemoryPartitionSpecs : AdapterPartitionSpecsBase<InMemoryAdapterFactory>
{
    public InMemoryPartitionSpecs(InMemoryAdapterFactory factory) : base(factory) { }
}
