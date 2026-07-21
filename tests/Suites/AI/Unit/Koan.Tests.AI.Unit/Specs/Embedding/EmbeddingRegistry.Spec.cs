using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

namespace Koan.Tests.AI.Unit.Specs.Embedding;

/// <summary>
/// Proves that embedding discovery retains immutable process-wide type facts only.
/// </summary>
[Trait("ADR", "AI-0021")]
[Trait("Category", "Unit")]
public sealed class EmbeddingRegistrySpec : IDisposable
{
    public EmbeddingRegistrySpec()
    {
        EmbeddingRegistry.ResetForTesting();
    }

    public void Dispose()
    {
        EmbeddingRegistry.ResetForTesting();
    }

    [Fact]
    public void RegisterTypes_records_process_type_facts_idempotently()
    {
        EmbeddingRegistry.RegisterTypes(
            [typeof(AsyncDocument), typeof(SyncDocument), typeof(AsyncDocument)]);

        var registered = EmbeddingRegistry.GetRegisteredTypes();

        registered.Should().HaveCount(2);
        registered.Should().Contain(typeof(AsyncDocument));
        registered.Should().Contain(typeof(SyncDocument));
    }

    [Fact]
    public void AsyncEntityTypes_filters_using_immutable_attribute_metadata()
    {
        EmbeddingRegistry.RegisterTypes([typeof(AsyncDocument), typeof(SyncDocument)]);

        var asyncTypes = EmbeddingRegistry.AsyncEntityTypes.ToList();

        asyncTypes.Should().ContainSingle().Which.Should().Be(typeof(AsyncDocument));
    }

    [Fact]
    public void RegisterTypes_ignores_null_collections_and_entries()
    {
        EmbeddingRegistry.RegisterTypes(null!);
        EmbeddingRegistry.RegisterTypes([typeof(AsyncDocument), null!]);

        EmbeddingRegistry.GetRegisteredTypes()
            .Should().ContainSingle().Which.Should().Be(typeof(AsyncDocument));
    }

    [Embedding(Async = true)]
    private sealed class AsyncDocument : Entity<AsyncDocument>
    {
    }

    [Embedding(Async = false)]
    private sealed class SyncDocument : Entity<SyncDocument>
    {
    }
}
