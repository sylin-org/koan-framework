using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Json.Tests;

public sealed class JsonTransferSpecs : AdapterTransferSpecsBase<JsonAdapterFactory>
{
    public JsonTransferSpecs(JsonAdapterFactory factory) : base(factory) { }

    // DATA-0107 lists this adapter under "corrective rejection", not provider-bounded streams.
    protected override bool ProviderStreamsAreBounded => false;

}
