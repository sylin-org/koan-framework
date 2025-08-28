using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Sora.Secrets.Abstractions;

namespace Sora.Secrets.Core.Resolver;

public sealed class ChainSecretResolver : ISecretResolver
{
    private readonly IReadOnlyList<ISecretProvider> _providers;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ChainSecretResolver>? _logger;

    private static readonly Regex Placeholder = new(@"\$\{(secret(?:\+[a-zA-Z0-9_-]+)?://[^}]+)}", RegexOptions.Compiled);

    public ChainSecretResolver(IEnumerable<ISecretProvider> providers, IMemoryCache cache, ILogger<ChainSecretResolver>? logger = null)
    {
        _providers = providers.ToArray();
        _cache = cache;
        _logger = logger;
    }

    public async Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
    {
        var cacheKey = ($"{id}");
    if (_cache.TryGetValue<SecretValue>(cacheKey, out var cached) && cached is not null) return cached;

        foreach (var p in _providers)
        {
            try
            {
                var v = await p.GetAsync(id, ct).ConfigureAwait(false);
                var ttl = v.Meta.Ttl ?? TimeSpan.FromMinutes(5);
                _cache.Set(cacheKey, v, ttl);
                return v;
            }
            catch (SecretNotFoundException)
            {
                // try next
            }
        }
        throw new SecretNotFoundException(id.ToString());
    }

    public async Task<string> ResolveAsync(string template, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;
        // quick scan to short-circuit when no placeholders
        _ = Placeholder.Replace(template, m => m.Groups[1].Value);
        // Now each matched token is a full secret URI
        var matches = Placeholder.Matches(template);
        if (matches.Count == 0) return template;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in matches.Cast<Match>())
        {
            var token = match.Groups[1].Value;
            var id = SecretId.Parse(token);
            var v = await GetAsync(id, ct).ConfigureAwait(false);
            map[token] = v.AsString();
        }
    var s = template;
        foreach (var kv in map)
        {
            s = s.Replace("${" + kv.Key + "}", kv.Value, StringComparison.Ordinal);
        }
        return s;
    }
}
