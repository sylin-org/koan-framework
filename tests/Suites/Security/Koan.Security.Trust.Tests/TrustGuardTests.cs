using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Koan.Security.Trust;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0003 §2.5: the fail-closed boot guard. Real-deployment environments (Production / Staging) refuse to
/// start on the default insecure shared secret; a custom key, a configured issuer, or the explicit escape flag
/// boots; Development and test environments boot. (Tested by invoking TrustModule.Start directly; composition
/// through real AddKoan is covered by AuthTrustFabricSpec.)
/// </summary>
public sealed class TrustGuardTests
{
    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static Func<Task> Start(string environment, params (string Key, string? Value)[] settings)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in settings) dict[key] = value;
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Env { EnvironmentName = environment });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(dict).Build());
        var sp = services.BuildServiceProvider();
        return () => new TrustModule().Start(sp, CancellationToken.None);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Real_deployment_with_default_insecure_key_refuses_to_boot(string environment)
        => await Start(environment).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*fail-closed*" + environment + "*");

    [Fact]
    public async Task Production_with_escape_flag_boots()
        => await Start("Production", ("Koan:Security:Trust:AllowInsecureKeyInProduction", "true"))
            .Should().NotThrowAsync();

    [Fact]
    public async Task Production_with_a_custom_shared_key_boots()
        => await Start("Production", ("Koan:Security:Trust:Key", "a-real-deployment-secret"))
            .Should().NotThrowAsync();

    [Fact]
    public async Task Production_with_configured_issuer_boots()
        => await Start("Production", ("Koan:Security:Trust:Issuer", "https://idp.example.com"))
            .Should().NotThrowAsync();

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task Dev_and_test_boot_with_the_default_key(string environment)
        => await Start(environment).Should().NotThrowAsync();
}
