using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Extensions;

public static class TestContextPostgresExtensions
{
    public static bool TryGetPostgresFixture(this TestContext context, out PostgresContainerFixture fixture, string key = "postgres")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryGetItem(key, out fixture!);
    }

    public static PostgresContainerFixture GetPostgresFixture(this TestContext context, string key = "postgres")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredItem<PostgresContainerFixture>(key);
    }
}
