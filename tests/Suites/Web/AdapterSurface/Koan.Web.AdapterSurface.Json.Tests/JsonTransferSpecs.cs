using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Json.Tests;

public sealed class JsonTransferSpecs : AdapterTransferSpecsBase<JsonAdapterFactory>
{
    public JsonTransferSpecs(JsonAdapterFactory factory) : base(factory) { }
}
