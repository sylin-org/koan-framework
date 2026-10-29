using AwesomeAssertions;
using Koan.Web.Auth.Infrastructure;
using Xunit;

namespace Koan.Web.Auth.Tests;

/// <summary>
/// WEB-0080: a failure landing is a rooted path of this application. After percent-decoding it names no host, no
/// <c>.</c> or <c>..</c> segment, no backslash, no encoded slash, no control character and nothing outside printable
/// ASCII, and its query does not name <c>error</c>; so no accepted landing can leave the base path, carry a second
/// code, or fail as a <c>Location</c> header.
/// </summary>
public sealed class SignInFailureReturnTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/sign-in")]
    [InlineData("/sign-in?from=home")]
    [InlineData("/sign-in?from=a%20b&next=%2Fhome")]
    [InlineData("/a.b/..c/d..")]
    [InlineData("/~user/it's(1)!*+,;=:@$")]
    public void A_rooted_path_of_the_application_is_accepted(string landing)
    {
        SignInFailureReturn.TryAccept(landing, out var path, out var problem).Should().BeTrue(problem);
        path.Should().Be(landing);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("sign-in", "does not start with '/'")]
    [InlineData("https://evil.example/sign-in", "does not start with '/'")]
    [InlineData("//evil.example/sign-in", "names a host")]
    [InlineData("/\\evil.example", "backslash")]
    [InlineData("/sign\\in", "backslash")]
    [InlineData("/../admin", "'..' segment")]
    [InlineData("/%2e%2e/admin", "'..' segment")]
    [InlineData("/.%2E/admin", "'..' segment")]
    [InlineData("/a/./b", "'..' segment")]
    [InlineData("/a/..", "'..' segment")]
    [InlineData("/a%2Fb", "encodes a slash")]
    [InlineData("/%2f%2fevil.example", "encodes a slash")]
    [InlineData("/a%5Cb", "encodes a slash or a backslash")]
    [InlineData("/é", "outside printable ASCII")]
    [InlineData("/\u2215admin", "outside printable ASCII")]
    [InlineData("/%C3%A9", "outside printable ASCII")]
    [InlineData("/sign-in?x=%C3%A9", "outside printable ASCII")]
    [InlineData("/sign in", "outside printable ASCII")]
    [InlineData("/sign-in\r\nX-Injected: 1", "control character")]
    [InlineData("/a%0d%0ab", "control character")]
    [InlineData("/a%zz", "does not encode a byte")]
    [InlineData("/a%2", "does not encode a byte")]
    [InlineData("/sign-in#top", "does not carry")]
    [InlineData("/sign-in?next=<script>", "does not carry")]
    [InlineData("/sign-in?error=forged", "names 'error'")]
    [InlineData("/sign-in?from=home&Error=forged", "names 'error'")]
    [InlineData("/sign-in?%65rror=forged", "names 'error'")]
    public void A_landing_that_could_leave_the_application_or_fail_as_a_header_is_refused(string landing, string named)
    {
        SignInFailureReturn.TryAccept(landing, out var path, out var problem).Should().BeFalse();
        path.Should().BeNull();
        problem.Should().Contain(named);
    }
}
