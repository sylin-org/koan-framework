using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Sora.Data.Mongo.Tests;

internal static class TestMongoTeardown
{
    public static async Task DropDatabaseAsync(IServiceProvider sp)
    {
        try
        {
            var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.ConnectionString) && !string.IsNullOrWhiteSpace(opts.Database))
            {
                var client = new MongoClient(opts.ConnectionString);
                await client.DropDatabaseAsync(opts.Database);
            }
        }
        catch
        {
            // best-effort; ignore cleanup failures (e.g., bad connection)
        }
    }
}
