using Koan.Cache.Abstractions.Primitives;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CacheKeyForSpec
{
    private sealed class Todo { }
    private sealed class Product { }
    private sealed class Track<T> { }

    [Fact]
    public void For_generic_with_id_and_partition_produces_canonical_key()
    {
        var key = CacheKey.For<Todo>("abc-123", partition: "archive");

        key.Value.Should().Be("Todo:archive:abc-123");
    }

    [Fact]
    public void For_generic_with_null_partition_uses_underscore_sentinel()
    {
        var key = CacheKey.For<Todo>("abc-123");

        key.Value.Should().Be("Todo:_:abc-123");
    }

    [Fact]
    public void For_generic_with_whitespace_partition_uses_underscore_sentinel()
    {
        var key = CacheKey.For<Todo>("abc-123", partition: "   ");

        key.Value.Should().Be("Todo:_:abc-123");
    }

    [Fact]
    public void For_non_generic_takes_arbitrary_Type()
    {
        var key = CacheKey.For(typeof(Product), "xyz", partition: "tenant-7");

        key.Value.Should().Be("Product:tenant-7:xyz");
    }

    [Fact]
    public void For_with_int_id_uses_ToString()
    {
        var key = CacheKey.For<Todo>(42);

        key.Value.Should().Be("Todo:_:42");
    }

    [Fact]
    public void For_with_guid_id_uses_ToString()
    {
        var guid = new Guid("019a0000-0000-7000-8000-000000000001");
        var key = CacheKey.For<Todo>(guid);

        key.Value.Should().Be($"Todo:_:{guid}");
    }

    [Fact]
    public void For_null_id_throws()
    {
        var act = () => CacheKey.For<Todo>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void For_null_entityType_throws()
    {
        var act = () => CacheKey.For(null!, "id");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void For_partition_with_whitespace_around_trimmed()
    {
        var key = CacheKey.For<Todo>("abc", partition: "  archive  ");

        key.Value.Should().Be("Todo:archive:abc");
    }

    [Fact]
    public void Different_partitions_produce_different_keys_for_same_id()
    {
        var keyA = CacheKey.For<Todo>("abc", partition: "a");
        var keyB = CacheKey.For<Todo>("abc", partition: "b");

        keyA.Should().NotBe(keyB);
    }

    [Fact]
    public void EntityTypeName_non_generic_is_the_type_name()
    {
        CacheKey.EntityTypeName(typeof(Todo)).Should().Be("Todo");
    }

    [Fact]
    public void EntityTypeName_closed_generic_appends_type_args()
    {
        // Type.Name alone yields "Track`1" for BOTH — these must differ.
        CacheKey.EntityTypeName(typeof(Track<Todo>)).Should().Be("Track<Todo>");
        CacheKey.EntityTypeName(typeof(Track<Product>)).Should().Be("Track<Product>");
    }

    [Fact]
    public void EntityTypeName_nested_generic_recurses()
    {
        CacheKey.EntityTypeName(typeof(Track<Track<Todo>>)).Should().Be("Track<Track<Todo>>");
    }

    [Fact]
    public void For_closed_generics_with_different_args_do_not_collide()
    {
        // The X-generic-typeid-ephemeral guard: Type.Name would collapse both to "Track`1:_:1".
        var a = CacheKey.For(typeof(Track<Todo>), "1");
        var b = CacheKey.For(typeof(Track<Product>), "1");

        a.Should().NotBe(b);
    }
}
