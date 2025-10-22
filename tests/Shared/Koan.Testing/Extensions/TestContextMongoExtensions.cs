using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Extensions;

public static class TestContextMongoExtensions
{
    public static bool TryGetMongoFixture(this TestContext context, out MongoContainerFixture fixture, string key = "mongo")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryGetItem(key, out fixture!);
    }

    public static MongoContainerFixture GetMongoFixture(this TestContext context, string key = "mongo")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredItem<MongoContainerFixture>(key);
    }
}
