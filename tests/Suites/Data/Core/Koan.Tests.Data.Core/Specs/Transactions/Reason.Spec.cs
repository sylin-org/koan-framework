using AwesomeAssertions;
using Koan.Data.Core.Commands;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>ARCH-0142: a plain string stays a reason, a string compared with a reason compares its text whatever its kind, and no string is no reason.</summary>
public sealed class ReasonSpec
{
    private static readonly Reason Locked = Reason.Gated("locked", "This topic is locked.", heading: "Locked");
    private const string NeedsBody = "A body holds at least one character.";

    private static Reason? Check(bool bad) => bad ? NeedsBody : null;

    [Fact]
    public void A_string_compared_with_a_reason_compares_its_text_whatever_its_kind()
    {
        ("This topic is locked." == Locked).Should().BeTrue();
        (Locked == "This topic is locked.").Should().BeTrue();
        ("This topic is locked." != Locked).Should().BeFalse();
        (Locked != "something else").Should().BeTrue();
        (Reason.Absent("Gone.") == "Gone.").Should().BeTrue();
    }

    [Fact]
    public void Two_reasons_are_equal_only_with_the_same_kind_and_a_null_reads_as_one()
    {
        Reason plain = "Gone.";
        (plain == Reason.Absent("Gone.")).Should().BeFalse();
        (plain == (Reason)"Gone.").Should().BeTrue();
        Reason? none = null;
        (none == null).Should().BeTrue();
        (Locked == null).Should().BeFalse();
    }

    [Fact]
    public void No_string_is_no_reason()
    {
        string? none = null;
        Reason? converted = none;
        converted.Should().BeNull();
        Check(false).Should().BeNull();
        Check(true)!.Kind.Should().Be(ReasonKind.Invalid);
    }

    [Fact]
    public void A_gate_carries_its_heading()
    {
        Locked.Heading.Should().Be("Locked");
        Reason.Gated("The rule.").Heading.Should().BeNull();
    }
}
