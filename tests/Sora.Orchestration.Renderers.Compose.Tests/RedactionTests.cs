using FluentAssertions;
using Sora.Orchestration;
using Xunit;

public class RedactionTests
{
    [Theory]
    [InlineData("password", "abc", "***")]
    [InlineData("Password", "abc", "***")]
    [InlineData("secret", "abc", "***")]
    [InlineData("pwd", "abc", "***")]
    [InlineData("token", "abc", "***")]
    [InlineData("apikey", "abc", "***")]
    [InlineData("connectionString", "abc", "***")]
    [InlineData("username", "abc", "abc")]
    [InlineData(null, "abc", "abc")]
    public void Redacts_sensitive_keys(string? key, string? value, string? expected)
    {
        Redaction.Maybe(key, value).Should().Be(expected);
    }
}
