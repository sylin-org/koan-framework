using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Redis.Tests;

public sealed class RedisPartitionSpecs : AdapterPartitionSpecsBase<RedisAdapterFactory>
{
    public RedisPartitionSpecs(RedisAdapterFactory factory) : base(factory) { }
}
