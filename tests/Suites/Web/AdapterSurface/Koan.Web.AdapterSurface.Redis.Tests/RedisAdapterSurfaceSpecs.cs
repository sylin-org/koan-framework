using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Redis.Tests;

public sealed class RedisAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<RedisAdapterFactory>
{
    public RedisAdapterSurfaceSpecs(RedisAdapterFactory factory) : base(factory) { }
}
