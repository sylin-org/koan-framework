using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Koan.Web.Auth.Connector.Test.Options;

// Internal DTOs to carry dev auth extras
namespace Koan.Web.Auth.Connector.Test.Infrastructure;

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
    private readonly IOptions<TestProviderOptions> _options;
    private readonly JwtTokenService _jwtService;
    private readonly ILogger<DevTokenStore> _logger;

    public DevTokenStore(IOptions<TestProviderOptions> options, JwtTokenService jwtService, ILogger<DevTokenStore> logger)
    {
        _options = options;
        _jwtService = jwtService;
        _logger = logger;
    }

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

        var code = Guid.CreateVersion7().ToString("n");
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
        var options = _options.Value;

        if (options.UseJwtTokens)
        {
            return _jwtService.CreateToken(profile, env, options);
        }
        else
        {
            return IssueHashToken(profile, ttl, env);
        }
    }

    private string IssueHashToken(UserProfile profile, TimeSpan ttl, ClaimEnvelope env)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{profile.Username}|{profile.Email}|{Guid.CreateVersion7():n}")));
        _tokens[token] = (DateTimeOffset.UtcNow.Add(ttl), profile, env);
        return token;
    }

    public bool TryGetToken(string token, out UserProfile profile, out ClaimEnvelope env)
    {
        profile = default!; env = new ClaimEnvelope();

        // Try hash-based token first (existing behavior)
        if (TryGetHashToken(token, out profile, out env))
        {
            return true;
        }

        // Try JWT validation if enabled and token looks like a JWT (has 3 parts separated by dots)
        var options = _options.Value;
        if (options.UseJwtTokens && IsJwtFormat(token))
        {
            return _jwtService.ValidateToken(token, options, out profile, out env);
        }

        return false;
    }

    private bool TryGetHashToken(string token, out UserProfile profile, out ClaimEnvelope env)
    {
        profile = default!; env = new ClaimEnvelope();
        if (!_tokens.TryGetValue(token, out var entry)) return false;
        if (entry.Exp < DateTimeOffset.UtcNow)
        {
            _tokens.TryRemove(token, out _);
            return false;
        }
        profile = entry.Profile;
        env = entry.Env;
        return true;
    }

    private static bool IsJwtFormat(string token)
    {
        // Basic check: JWT has 3 parts separated by dots
        return !string.IsNullOrWhiteSpace(token) &&
               token.Split('.').Length == 3 &&
               !token.All(char.IsDigit) && // Not a hex string
               !token.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')); // Not hex
    }
}
