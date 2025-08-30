using System.Collections.Concurrent;

// Internal DTOs to carry dev auth extras
namespace Sora.Web.Auth.TestProvider.Infrastructure;

public sealed class DevTokenStore
{
    public sealed class ClaimEnvelope
    {
        public HashSet<string> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Permissions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Claims { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly ConcurrentDictionary<string, (DateTimeOffset Exp, UserProfile Profile, string? CodeChallenge, ClaimEnvelope Env)> _codes = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset Exp, UserProfile Profile, ClaimEnvelope Env)> _tokens = new();

    public string IssueCode(UserProfile profile, TimeSpan ttl, string? codeChallenge,
        ISet<string>? roles = null, ISet<string>? permissions = null, IDictionary<string, string[]>? claims = null)
    {
        var env = new ClaimEnvelope();
        if (roles != null) foreach (var r in roles) if (!string.IsNullOrWhiteSpace(r)) env.Roles.Add(r);
        if (permissions != null) foreach (var p in permissions) if (!string.IsNullOrWhiteSpace(p)) env.Permissions.Add(p);
        if (claims != null)
        {
            foreach (var kvp in claims)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                if (!env.Claims.TryGetValue(kvp.Key, out var list)) env.Claims[kvp.Key] = list = new List<string>();
                foreach (var v in kvp.Value ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(v)) continue;
                    if (!list.Contains(v)) list.Add(v);
                }
            }
        }

        var code = Guid.NewGuid().ToString("n");
        _codes[code] = (DateTimeOffset.UtcNow.Add(ttl), profile, codeChallenge, env);
        return code;
    }

    public bool TryRedeemCode(string code, out UserProfile profile, out string? challenge, out ClaimEnvelope env)
    {
        profile = default!; challenge = null; env = new ClaimEnvelope();
        if (!_codes.TryRemove(code, out var entry)) return false;
        if (entry.Exp < DateTimeOffset.UtcNow) return false;
        profile = entry.Profile; challenge = entry.CodeChallenge; env = entry.Env;
        return true;
    }

    public string IssueToken(UserProfile profile, TimeSpan ttl, ClaimEnvelope env)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{profile.Username}|{profile.Email}|{Guid.NewGuid():n}")));
        _tokens[token] = (DateTimeOffset.UtcNow.Add(ttl), profile, env);
        return token;
    }

    public bool TryGetToken(string token, out UserProfile profile, out ClaimEnvelope env)
    {
        profile = default!; env = new ClaimEnvelope();
        if (!_tokens.TryGetValue(token, out var entry)) return false;
        if (entry.Exp < DateTimeOffset.UtcNow) { _tokens.TryRemove(token, out _); return false; }
        profile = entry.Profile; env = entry.Env; return true;
    }
}