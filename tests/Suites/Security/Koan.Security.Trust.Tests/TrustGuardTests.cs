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
/// SEC-0001 Phase 2 (2g): the fail-closed boot guard. Production + the ephemeral in-process issuer + no
/// configured issuer refuses to start; a configured issuer or the explicit escape flag boots; non-production
/// always boots. (Tested by invoking TrustModule.Start directly — a full-AddKoan Production boot would also
/// trip unrelated production guards; composition through real AddKoan is covered by AuthTrustFabricSpec.)
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

    [Fact]
    public async Task Production_with_ephemeral_issuer_and_no_config_refuses_to_boot()
        => await Start("Production").Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*fail-closed*Production*");

    [Fact]
    public async Task Production_with_escape_flag_boots()
        => await Start("Production", ("Koan:Security:Trust:AllowEphemeralIssuerInProduction", "true"))
            .Should().NotThrowAsync();

    [Fact]
    public async Task Production_with_configured_issuer_boots()
        => await Start("Production", ("Koan:Security:Trust:Issuer", "https://idp.example.com"))
            .Should().NotThrowAsync();

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task Non_production_boots_with_ephemeral_issuer(string environment)
        => await Start(environment).Should().NotThrowAsync();
}
