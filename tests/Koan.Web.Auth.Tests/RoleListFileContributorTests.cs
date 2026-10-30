using System.Security.Claims;
using AwesomeAssertions;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Contributors.Builtin;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Web.Auth.Tests;

/// <summary>The role-list file grants on an email the provider verified, never on one a person typed.</summary>
public sealed class RoleListFileContributorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"koan-role-list-{Guid.CreateVersion7():n}.json");

    public RoleListFileContributorTests()
        => File.WriteAllText(_path, """{ "allow": { "ops@company.example": ["admin"] }, "revoke": { "ops@company.example": ["guest"] } }""");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task An_unverified_email_is_granted_nothing()
    {
        var identity = Identity(new Claim(ClaimTypes.Email, "ops@company.example"), new Claim(ClaimTypes.Role, "guest"));

        await SignIn(identity);

        identity.HasClaim(ClaimTypes.Role, "admin").Should().BeFalse();
        identity.HasClaim(ClaimTypes.Role, "guest").Should().BeTrue("nor does an unverified address revoke anything");
    }

    [Fact]
    public async Task A_verified_email_is_keyed_whichever_claim_carries_it()
    {
        foreach (var type in new[] { ClaimTypes.Email, "email" })
        {
            var identity = Identity(new Claim(type, "OPS@company.example"), new Claim(SignInProof.EmailVerifiedClaim, "true"), new Claim(ClaimTypes.Role, "guest"));

            await SignIn(identity);

            identity.HasClaim(ClaimTypes.Role, "admin").Should().BeTrue();
            identity.HasClaim(ClaimTypes.Role, "guest").Should().BeFalse();
        }
    }

    private static ClaimsIdentity Identity(params Claim[] claims) => new(claims, "test");

    private Task SignIn(ClaimsIdentity identity)
    {
        var options = new AuthLifecycleOptions { RoleListFile = { FilePath = _path } };
        var contributor = new RoleListFileContributor(new Fixed(options), NullLogger<RoleListFileContributor>.Instance);
        return contributor.OnSignIn(new AuthSignInContext
        {
            Provider = "probe",
            Identity = identity,
            Services = new ServiceCollection().BuildServiceProvider(),
            HttpContext = new DefaultHttpContext(),
        }, CancellationToken.None);
    }

    private sealed class Fixed(AuthLifecycleOptions value) : IOptionsMonitor<AuthLifecycleOptions>
    {
        public AuthLifecycleOptions CurrentValue => value;
        public AuthLifecycleOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AuthLifecycleOptions, string?> listener) => null;
    }
}
