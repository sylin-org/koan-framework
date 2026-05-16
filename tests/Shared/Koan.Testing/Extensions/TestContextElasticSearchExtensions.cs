using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Extensions;

public static class TestContextElasticSearchExtensions
{
    public static bool TryGetElasticSearchFixture(this TestContext context, out ElasticSearchContainerFixture fixture, string key = "elasticsearch")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryGetItem(key, out fixture!);
    }

    public static ElasticSearchContainerFixture GetElasticSearchFixture(this TestContext context, string key = "elasticsearch")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredItem<ElasticSearchContainerFixture>(key);
    }
}
