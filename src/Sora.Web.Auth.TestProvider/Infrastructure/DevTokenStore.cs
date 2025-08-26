using System.Collections.Concurrent;

namespace Sora.Web.Auth.TestProvider.Infrastructure;

public sealed class DevTokenStore
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Exp, UserProfile Profile, string? CodeChallenge)> _codes = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset Exp, UserProfile Profile)> _tokens = new();

    public string IssueCode(UserProfile profile, TimeSpan ttl, string? codeChallenge)
    {
        var code = Guid.NewGuid().ToString("n");
        _codes[code] = (DateTimeOffset.UtcNow.Add(ttl), profile, codeChallenge);
        return code;
    }

    public bool TryRedeemCode(string code, out UserProfile profile, out string? challenge)
    {
        profile = default!; challenge = null;
        if (!_codes.TryRemove(code, out var entry)) return false;
        if (entry.Exp < DateTimeOffset.UtcNow) return false;
        profile = entry.Profile; challenge = entry.CodeChallenge;
        return true;
    }

    public string IssueToken(UserProfile profile, TimeSpan ttl)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{profile.Username}|{profile.Email}|{Guid.NewGuid():n}")));
        _tokens[token] = (DateTimeOffset.UtcNow.Add(ttl), profile);
        return token;
    }

    public bool TryGetProfile(string token, out UserProfile profile)
    {
        profile = default!;
        if (!_tokens.TryGetValue(token, out var entry)) return false;
        if (entry.Exp < DateTimeOffset.UtcNow) { _tokens.TryRemove(token, out _); return false; }
        profile = entry.Profile; return true;
    }
}