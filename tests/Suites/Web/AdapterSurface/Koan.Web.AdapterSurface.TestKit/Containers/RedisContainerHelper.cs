using StackExchange.Redis;
using Koan.Testing.Containers;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class RedisContainerHelper : KoanWebContainerHelper<RedisFixture>
{
    public async Task ResetAsync()
    {
        if (ConnectionString is null) return;
        var muxer = await ConnectionMultiplexer.ConnectAsync($"{ConnectionString},allowAdmin=true").ConfigureAwait(false);
        try
        {
            var endpoints = muxer.GetEndPoints();
            foreach (var ep in endpoints)
            {
                var server = muxer.GetServer(ep);
                await server.FlushDatabaseAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await muxer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
