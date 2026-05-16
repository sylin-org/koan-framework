using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Extensions;

public static class TestContextOpenSearchExtensions
{
    public static bool TryGetOpenSearchFixture(this TestContext context, out OpenSearchContainerFixture fixture, string key = "opensearch")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryGetItem(key, out fixture!);
    }

    public static OpenSearchContainerFixture GetOpenSearchFixture(this TestContext context, string key = "opensearch")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredItem<OpenSearchContainerFixture>(key);
    }
}
