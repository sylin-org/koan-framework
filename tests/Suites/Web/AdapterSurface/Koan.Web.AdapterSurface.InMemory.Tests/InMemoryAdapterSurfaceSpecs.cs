using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.InMemory.Tests;

public sealed class InMemoryAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<InMemoryAdapterFactory>
{
    public InMemoryAdapterSurfaceSpecs(InMemoryAdapterFactory factory) : base(factory) { }
}
