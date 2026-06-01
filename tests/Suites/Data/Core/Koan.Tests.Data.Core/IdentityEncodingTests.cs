using Koan.Data.Core.Model;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Relationships;

namespace Koan.Tests.Data.Core;

/// <summary>
/// Unit specs for the DATA-0098 selection seam: encoding is chosen per-field from static metadata
/// (entity id strategy + declared parent references), never by inspecting a value.
/// </summary>
public class IdentityEncodingTests
{
    private sealed class GuidEntity : Entity<GuidEntity> { }                 // Entity<T> -> GUID identity
    private sealed class SlugEntity : Entity<SlugEntity, string> { }         // explicit string -> no optimization

    private sealed class WithGuidRef : Entity<WithGuidRef>
    {
        [Parent(typeof(GuidEntity))] public string? OwnerId { get; set; }
        public string Note { get; set; } = "";                              // a plain string that may *look* like a guid
    }

    private sealed class WithSlugRef : Entity<WithSlugRef>
    {
        [Parent(typeof(SlugEntity))] public string? SlugOwnerId { get; set; }
    }

    [Fact]
    public void Guid_entity_id_is_encoded()
        => IdentityEncoding.IsGuidEncoded(typeof(GuidEntity), "Id").Should().BeTrue();

    [Fact]
    public void String_keyed_entity_has_no_encoded_members()
        => IdentityEncoding.GuidEncodedMembers(typeof(SlugEntity)).Should().BeEmpty();

    [Fact]
    public void Reference_to_guid_entity_is_encoded_but_a_plain_string_is_not()
    {
        var members = IdentityEncoding.GuidEncodedMembers(typeof(WithGuidRef));
        members.Should().Contain("Id");        // the entity's own GUID id
        members.Should().Contain("OwnerId");   // a declared parent ref to a GUID-identity entity
        members.Should().NotContain("Note");   // not an id/ref -> stays a plain string (no over-reach)
    }

    [Fact]
    public void Reference_to_string_keyed_entity_is_not_encoded()
    {
        var members = IdentityEncoding.GuidEncodedMembers(typeof(WithSlugRef));
        members.Should().Contain("Id");                // WithSlugRef is Entity<T> -> GUID id
        members.Should().NotContain("SlugOwnerId");    // parent is string-keyed -> ref stays a string
    }
}
