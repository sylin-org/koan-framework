using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Sora.Secrets.Abstractions;
using Sora.Secrets.Core.Resolver;
using Xunit;

namespace Sora.Secrets.Core.Tests;

public class SecretsCachingTests
{
    private sealed class CountingProvider : ISecretProvider
    {
        private int _count;
        private readonly TimeSpan _ttl;
        public int Calls => _count;

        public CountingProvider(TimeSpan ttl)
        {
            _ttl = ttl;
        }

        public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
        {
            var n = Interlocked.Increment(ref _count);
            var val = Encoding.UTF8.GetBytes($"v{n}");
            var meta = new SecretMetadata { Ttl = _ttl };
            return Task.FromResult(new SecretValue(val, SecretContentType.Text, meta));
        }
    }

    [Fact]
    public async Task Cache_respects_ttl_and_refreshes_after_expiry()
    {
        var ttl = TimeSpan.FromMilliseconds(60);
        var provider = new CountingProvider(ttl);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new ChainSecretResolver(new[] { provider }, cache);
        var id = SecretId.Parse("secret://app/key");

        var v1 = await resolver.GetAsync(id);
        var s1 = v1.AsString();
        s1.Should().Be("v1");
        provider.Calls.Should().Be(1);

        var v2 = await resolver.GetAsync(id);
        var s2 = v2.AsString();
        s2.Should().Be("v1"); // cached
        provider.Calls.Should().Be(1);

        await Task.Delay(ttl + TimeSpan.FromMilliseconds(50));

        var v3 = await resolver.GetAsync(id);
        var s3 = v3.AsString();
        s3.Should().Be("v2"); // refreshed
        provider.Calls.Should().Be(2);
    }

    [Fact]
    public void SecretValue_ToString_is_redacted()
    {
        var sv = new SecretValue(Encoding.UTF8.GetBytes("pw"), SecretContentType.Text);
        sv.ToString().Should().Be("***");
    }
}
