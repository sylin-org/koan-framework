using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.InMemory.Tests;

public sealed class InMemoryTransferSpecs : AdapterTransferSpecsBase<InMemoryAdapterFactory>
{
    public InMemoryTransferSpecs(InMemoryAdapterFactory factory) : base(factory) { }

    // DATA-0107 lists this adapter under "corrective rejection", not provider-bounded streams.
    protected override bool ProviderStreamsAreBounded => false;

}
