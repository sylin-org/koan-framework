using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Json.Tests;

public sealed class JsonPartitionSpecs : AdapterPartitionSpecsBase<JsonAdapterFactory>
{
    public JsonPartitionSpecs(JsonAdapterFactory factory) : base(factory) { }
}
