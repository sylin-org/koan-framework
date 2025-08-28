using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Sora.Secrets.Abstractions;
using Sora.Secrets.Core.Configuration;
using Sora.Secrets.Core.DI;
using Sora.Secrets.Core.Resolver;
using Xunit;

namespace Sora.Secrets.Core.Tests;

public class SecretsResolutionTests
{
    private sealed class DictProvider(ConcurrentDictionary<string, string> map) : ISecretProvider
    {
        public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
        {
            if (!map.TryGetValue(id.ToString(), out var v)) throw new SecretNotFoundException(id.ToString());
            return Task.FromResult(new SecretValue(System.Text.Encoding.UTF8.GetBytes(v), SecretContentType.Text, new SecretMetadata { Ttl = TimeSpan.FromSeconds(1) }));
        }
    }

    [Fact]
    public async Task Placeholder_resolution_with_bootstrap_chain_works()
    {
        var baseCfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnStr"] = "Host=pg;Password=${secret://db/main};Database=app",
        }).Build();

        var cfgBuilder = new ConfigurationBuilder();
        cfgBuilder.AddSecretsReferenceConfiguration(null);
        var cfg = cfgBuilder.Build();
        // base config is empty, bootstrap chain uses env+config; we emulate config provider via underlying baseCfg
        // Replace the default provider source with one using our baseCfg
        var src = new SecretResolvingConfigurationSource(null, baseCfg);
        cfgBuilder.Sources.Clear();
        cfgBuilder.Add(src);
        cfg = cfgBuilder.Build();

        // Inject secret via underlying config provider mapping: Secrets:db:main
        baseCfg["Secrets:db:main"] = "pw";

        var conn = cfg["ConnStr"];
        conn.Should().Be("Host=pg;Password=pw;Database=app");
    }

    [Fact]
    public async Task Whole_value_secret_resolution_works()
    {
        var baseCfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Db:Password"] = "secret://db/main",
        }).Build();
        var cfgBuilder = new ConfigurationBuilder();
        var src = new SecretResolvingConfigurationSource(null, baseCfg);
        cfgBuilder.Add(src);
        var cfg = cfgBuilder.Build();
        baseCfg["Secrets:db:main"] = "pw2";
        cfg["Db:Password"].Should().Be("pw2");
    }

    [Fact]
    public void Upgrade_emits_reload_and_uses_di_resolver()
    {
        // Arrange base config with placeholder
        var baseCfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnStr"] = "Host=pg;Password=${secret://db/main};Database=app",
        }).Build();
        var cfgBuilder = new ConfigurationBuilder();
        var src = new SecretResolvingConfigurationSource(null, baseCfg);
        cfgBuilder.Add(src);
        var cfg = cfgBuilder.Build();

        // No value yet via bootstrap → resolves to original template (no provider can serve)
        baseCfg["Secrets:db:main"] = null; // ensure not found in bootstrap config provider

        // Track reloads
        var reloaded = false;
        ChangeToken.OnChange(() => cfg.GetReloadToken(), () => reloaded = true);

        // Build DI with DictProvider-backed resolver
        var dict = new ConcurrentDictionary<string, string>(StringComparer.Ordinal)
        {
            [SecretId.Parse("secret://db/main").ToString()] = "from-di"
        };
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(baseCfg);
        services.AddSoraSecrets();
        services.AddSingleton<ISecretProvider>(_ => new DictProvider(dict));
        var sp = services.BuildServiceProvider();

        // Act: upgrade
        SecretResolvingConfigurationExtensions.UpgradeSecretsConfiguration(sp);

        // Assert: reload fired and resolution now uses DI resolver
        reloaded.Should().BeTrue();
        var val = cfg["ConnStr"];
        val.Should().Be("Host=pg;Password=from-di;Database=app");
    }
}
