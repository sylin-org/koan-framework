using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Redis.Tests;

public sealed class RedisTransferSpecs : AdapterTransferSpecsBase<RedisAdapterFactory>
{
    public RedisTransferSpecs(RedisAdapterFactory factory) : base(factory) { }
}
