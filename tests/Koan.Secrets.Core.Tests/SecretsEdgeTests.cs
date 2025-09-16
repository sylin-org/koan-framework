using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Secrets.Abstractions;
using Koan.Secrets.Core.Configuration;
using Koan.Secrets.Core.Resolver;
using Xunit;

namespace Koan.Secrets.Core.Tests;

public class SecretsEdgeTests
{
    [Fact]
    public void SecretId_parses_authority_and_path_forms()
    {
        var a = SecretId.Parse("secret://scope/name");
        a.Scope.Should().Be("scope");
        a.Name.Should().Be("name");
        a.Provider.Should().BeNull();

        var b = SecretId.Parse("secret+vault://team/api?version=7");
        b.Scope.Should().Be("team");
        b.Name.Should().Be("api");
        b.Version.Should().Be("7");
        b.Provider.Should().Be("vault");

        var c = SecretId.Parse("secret://app/key?version=1");
        c.Scope.Should().Be("app");
        c.Name.Should().Be("key");
        c.Version.Should().Be("1");

        // invalid: host without name segment
        Action act = () => SecretId.Parse("secret://scope");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Resolve_returns_input_when_no_placeholders()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new ChainSecretResolver(Array.Empty<ISecretProvider>(), cache);
        (await resolver.ResolveAsync("no-secrets")).Should().Be("no-secrets");
        (await resolver.ResolveAsync(string.Empty)).Should().Be(string.Empty);
    }

    private sealed class CountingProvider : ISecretProvider
    {
        private int _count;
        public int Calls => _count;
        public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
        {
            var n = Interlocked.Increment(ref _count);
            var bytes = Encoding.UTF8.GetBytes($"v{n}");
            return Task.FromResult(new SecretValue(bytes, SecretContentType.Text, new SecretMetadata { Ttl = TimeSpan.FromSeconds(5) }));
        }
    }

    [Fact]
    public async Task Resolve_multiple_placeholders_reuses_cached_value()
    {
        var provider = new CountingProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new ChainSecretResolver(new[] { provider }, cache);

        var text = "User=${secret://db/user};Pw=${secret://db/user}";
        var resolved = await resolver.ResolveAsync(text);
        resolved.Should().Be("User=v1;Pw=v1");
        provider.Calls.Should().Be(1);
    }

    private sealed class NotFoundProvider : ISecretProvider
    {
        public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
            => throw new SecretNotFoundException(id.ToString());
    }

    private sealed class FixedProvider(string value) : ISecretProvider
    {
        public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
            => Task.FromResult(new SecretValue(Encoding.UTF8.GetBytes(value), SecretContentType.Text));
    }

    [Fact]
    public async Task Provider_chain_falls_back_when_first_returns_not_found()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new ChainSecretResolver(new ISecretProvider[] { new NotFoundProvider(), new FixedProvider("ok") }, cache);
        var id = SecretId.Parse("secret://app/key");
        var sv = await resolver.GetAsync(id);
        sv.AsString().Should().Be("ok");
    }

    [Fact]
    public async Task GetAsync_throws_not_found_when_all_providers_fail()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new ChainSecretResolver(new ISecretProvider[] { new NotFoundProvider() }, cache);
        var id = SecretId.Parse("secret://missing/none");
        Func<Task> act = async () => await resolver.GetAsync(id);
        await act.Should().ThrowAsync<SecretNotFoundException>();
    }

    [Fact]
    public void Whole_value_secret_with_provider_scheme_resolves_via_boot_config()
    {
        var baseCfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Db:Password"] = "secret+vault://db/main",
            ["Secrets:db:main"] = "p@ss",
        }).Build();

        var cfgBuilder = new ConfigurationBuilder();
        cfgBuilder.Add(new SecretResolvingConfigurationSource(null, baseCfg));
        var cfg = cfgBuilder.Build();

        cfg["Db:Password"].Should().Be("p@ss");
    }
}
