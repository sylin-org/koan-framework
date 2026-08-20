using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Redis.Tests;

public sealed class RedisTransferSpecs : AdapterTransferSpecsBase<RedisAdapterFactory>
{
    public RedisTransferSpecs(RedisAdapterFactory factory) : base(factory) { }

    // DATA-0107 lists this adapter under "corrective rejection", not provider-bounded streams.
    protected override bool ProviderStreamsAreBounded => false;

}
