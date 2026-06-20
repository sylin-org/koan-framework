using System;
using AwesomeAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Koan.Web.Auth.Server.Keys;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>SEC-0006 D1 — the fail-closed guard: no production/staging AS on an ephemeral key unless acknowledged.</summary>
public sealed class IssuerKeyGuardTests
{
    private static IHostEnvironment Env(string name) => new FakeEnv { EnvironmentName = name };

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Throws_when_an_ephemeral_key_is_used_outside_development(string environment)
    {
        Action act = () => IssuerKeyGuard.EnsurePersistedOutsideDevelopment(isEphemeral: true, Env(environment), acknowledged: false);
        act.Should().Throw<InvalidOperationException>().WithMessage("*fail-closed*");
    }

    [Fact]
    public void Allows_an_ephemeral_key_in_development()
    {
        Action act = () => IssuerKeyGuard.EnsurePersistedOutsideDevelopment(isEphemeral: true, Env("Development"), acknowledged: false);
        act.Should().NotThrow();
    }

    [Fact]
    public void Allows_an_ephemeral_key_outside_development_when_explicitly_acknowledged()
    {
        Action act = () => IssuerKeyGuard.EnsurePersistedOutsideDevelopment(isEphemeral: true, Env("Production"), acknowledged: true);
        act.Should().NotThrow();
    }

    [Fact]
    public void Allows_a_persisted_key_in_production()
    {
        Action act = () => IssuerKeyGuard.EnsurePersistedOutsideDevelopment(isEphemeral: false, Env("Production"), acknowledged: false);
        act.Should().NotThrow();
    }

    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
