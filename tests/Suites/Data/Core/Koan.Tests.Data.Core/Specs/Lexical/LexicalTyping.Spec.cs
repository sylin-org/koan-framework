using Koan.Data.Abstractions.Lexical;

namespace Koan.Tests.Data.Core.Specs.Lexical;

/// <summary>
/// DATA-0165: a query declared as typed holds the word its text ends in as the one still being typed; a text that ends
/// in anything but a letter or digit holds none, and a query not declared as typed never does.
/// </summary>
public sealed class LexicalTypingSpec
{
    [Theory]
    [InlineData("lant", "lant")]
    [InlineData("copper lant", "lant")]
    [InlineData("  copper-lant", "lant")]
    [InlineData("lan copper Lan", "Lan")]
    [InlineData("lant ", null)]
    [InlineData("lant.", null)]
    [InlineData("lant -", null)]
    public void The_word_being_typed_is_the_one_the_text_ends_in(string text, string? typing)
    {
        new LexicalQuery(text, 1, 10, typed: true).Typing.Should().Be(typing);
        new LexicalQuery(text, 1, 10).Typing.Should().BeNull();
    }

    [Fact]
    public void A_term_repeated_in_another_case_is_the_word_being_typed()
    {
        var query = new LexicalQuery("Lan copper lan", 1, 10, typed: true);

        query.Terms.Should().Equal("Lan", "copper");
        query.IsTyping("Lan").Should().BeTrue();
        query.IsTyping("copper").Should().BeFalse();
    }
}
