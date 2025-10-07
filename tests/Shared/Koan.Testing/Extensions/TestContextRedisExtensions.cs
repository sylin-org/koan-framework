using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Extensions;

public static class TestContextRedisExtensions
{
    public static bool TryGetRedisFixture(this TestContext context, out RedisContainerFixture fixture, string key = "redis")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryGetItem(key, out fixture!);
    }

    public static RedisContainerFixture GetRedisFixture(this TestContext context, string key = "redis")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredItem<RedisContainerFixture>(key);
    }
}
