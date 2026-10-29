using AwesomeAssertions;
using Koan.Web.Auth.Server.Protocol;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0024 — <see cref="ConsentLookup.ClientNotAllowed"/> sits outside the record's positional members, so a page written
/// against SEC-0028's two-slot <see cref="ConsentLookup"/> still constructs and deconstructs it unchanged. This spec is
/// a compile-surface check: it does not build if either shape gains a slot.
/// </summary>
public sealed class ConsentLookupCompatibilitySpec
{
    [Fact]
    public void ConsentLookup_keeps_its_two_slot_constructor_and_deconstruction()
    {
        var lookup = new ConsentLookup(null, true);

        var (request, throttled) = lookup;

        request.Should().BeNull();
        throttled.Should().BeTrue();
        lookup.ClientNotAllowed.Should().BeFalse("a lookup says the client is not allowed only when Find found it so");
        (lookup with { ClientNotAllowed = true }).ClientNotAllowed.Should().BeTrue();
    }
}
