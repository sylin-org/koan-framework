using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Extensions;

public static class TestContextWeaviateExtensions
{
    public static bool TryGetWeaviateFixture(this TestContext context, out WeaviateContainerFixture fixture, string key = "weaviate")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryGetItem(key, out fixture!);
    }

    public static WeaviateContainerFixture GetWeaviateFixture(this TestContext context, string key = "weaviate")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredItem<WeaviateContainerFixture>(key);
    }
}
