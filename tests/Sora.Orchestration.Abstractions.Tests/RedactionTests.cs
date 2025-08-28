using FluentAssertions;
using Sora.Orchestration;
using Xunit;

public class RedactionTests
{
    [Theory]
    [InlineData("password=secret", "password=***")]
    [InlineData("Password = secret", "Password = ***")]
    [InlineData("connectionString: Host=localhost;Password=pw;", "connectionString: ***;Password=***;")]
    [InlineData("token=abc123 user=me", "token=*** user=me")]
    [InlineData("JWT=abc" , "JWT=abc")] // not matched
    public void RedactText_masks_sensitive_keys(string input, string expected)
    {
        Redaction.RedactText(input).Should().Be(expected);
    }
}
