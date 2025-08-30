using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.Roles.Contracts;
using Sora.Web.Auth.Roles.Infrastructure;
using Sora.Web.Auth.Roles.Options;

namespace Sora.Web.Auth.Roles.Services;

public sealed class DefaultRoleAttributionService : IRoleAttributionService
{
    private readonly IEnumerable<IRoleMapContributor> _contributors;
    private readonly IOptionsMonitor<RoleAttributionOptions> _options;
    private readonly ILogger<DefaultRoleAttributionService> _logger;
    private readonly IRoleConfigSnapshotProvider _snapshotProvider;
    private readonly IRoleBootstrapStateStore _bootstrap;
    private readonly IRoleAttributionCache _cache;

    public DefaultRoleAttributionService(IEnumerable<IRoleMapContributor> contributors, IOptionsMonitor<RoleAttributionOptions> options, ILogger<DefaultRoleAttributionService> logger, IRoleAttributionCache cache, IRoleConfigSnapshotProvider snapshotProvider, IRoleBootstrapStateStore bootstrap)
    {
        _contributors = contributors;
        _options = options;
        _logger = logger;
        _cache = cache;
        _snapshotProvider = snapshotProvider;
        _bootstrap = bootstrap;
    }

    public async Task<RoleAttributionResult> ComputeAsync(ClaimsPrincipal user, RoleAttributionContext? context = null, CancellationToken ct = default)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return RoleAttributionResult.Empty;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name ?? string.Empty;
        if (!string.IsNullOrEmpty(userId))
        {
            var cached = _cache.TryGet(userId);
            if (cached != null) return cached;
        }

        var opts = _options.CurrentValue;
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Extract raw roles from configured claim keys
        ExtractFromClaims(user, opts.ClaimKeys.Roles, roles);
        // 2) Extract permissions from configured keys
        ExtractFromClaims(user, opts.ClaimKeys.Permissions, perms);

    // 3) Normalize via aliases and cleanup (prefer DB snapshot, fallback to options)
    var aliases = _snapshotProvider.Get().Aliases;
    Normalize(roles, aliases.Count > 0 ? aliases : opts.Aliases.Map);
        Normalize(perms, null);

        // 4) Let contributors augment
        foreach (var c in _contributors)
        {
            try
            {
                await c.ContributeAsync(user, roles, perms, context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Role contributor {Contributor} threw; continuing", c.GetType().FullName);
            }
        }

        // 5) Bootstrap elevation gates (one-time)
        await TryApplyBootstrapAsync(user, roles, ct).ConfigureAwait(false);

        // 6) Dev fallback: ensure at least a reader role if configured and no roles found
        if (roles.Count == 0 && opts.DevFallback.Enabled && IsDevelopment())
            roles.Add(opts.DevFallback.Role);

        // 7) Caps
        Truncate(roles, opts.MaxRoles, nameof(opts.MaxRoles));
        Truncate(perms, opts.MaxPermissions, nameof(opts.MaxPermissions));

        // 8) Create stable stamp
        var stamp = CreateStamp(roles, perms);
    var result = new RoleAttributionResult(roles, perms, stamp);
    if (!string.IsNullOrEmpty(userId)) _cache.Set(userId, result);
    return result;
    }

    private async Task TryApplyBootstrapAsync(ClaimsPrincipal user, HashSet<string> roles, CancellationToken ct)
    {
    var opts = _options.CurrentValue;
    var mode = opts.Bootstrap?.Mode ?? "None";
        if (string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase)) return;

        // If an admin is already bootstrapped, do nothing
        try { if (await _bootstrap.IsAdminBootstrappedAsync(ct).ConfigureAwait(false)) return; }
        catch { return; }

        // Only elevate when the current principal lacks admin already
        if (roles.Contains("admin")) return;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId)) return;

        bool shouldElevate = false;
        if (string.Equals(mode, "FirstUser", StringComparison.OrdinalIgnoreCase))
        {
            // First authenticated user to hit this code path gets admin
            shouldElevate = true;
        }
        else if (string.Equals(mode, "ClaimMatch", StringComparison.OrdinalIgnoreCase))
        {
            var claimType = opts.Bootstrap?.ClaimType ?? ClaimTypes.Email;
            var values = new HashSet<string>(opts.Bootstrap?.ClaimValues ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var c in user.FindAll(claimType))
            {
                var v = c.Value;
                if (!string.IsNullOrWhiteSpace(v) && values.Contains(v)) { shouldElevate = true; break; }
            }
            // Also support AdminEmails[] convenience list targeting ClaimTypes.Email
            if (!shouldElevate && (opts.Bootstrap?.AdminEmails?.Length ?? 0) > 0)
            {
                var email = user.FindFirst(ClaimTypes.Email)?.Value;
                if (!string.IsNullOrWhiteSpace(email) && opts.Bootstrap!.AdminEmails!.Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase))) shouldElevate = true;
            }
        }

        if (!shouldElevate) return;

        try
        {
            await _bootstrap.MarkAdminBootstrappedAsync(userId, mode, ct).ConfigureAwait(false);
            roles.Add("admin");
            _logger.LogInformation("Sora.Web.Auth.Roles: admin bootstrap applied for {UserId} via {Mode}", userId, mode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sora.Web.Auth.Roles: failed to persist bootstrap state; skipping elevation");
        }
    }

    private static void ExtractFromClaims(ClaimsPrincipal user, string[] keys, ISet<string> output)
    {
        foreach (var key in keys)
        {
            foreach (var c in user.FindAll(key))
                AddSplit(output, c.Value);
        }
    }

    private static void AddSplit(ISet<string> set, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        // common separators: space, comma
        var parts = value.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var norm = NormalizeToken(p);
            if (!string.IsNullOrEmpty(norm)) set.Add(norm);
        }
    }

    private static string NormalizeToken(string value)
        => value.Trim().Replace(" ", "-").ToLowerInvariant();

    private static void Normalize(HashSet<string> set, IReadOnlyDictionary<string, string>? aliases)
    {
        if (set.Count == 0) return;
        var tmp = new HashSet<string>(set, set.Comparer);
        set.Clear();
        foreach (var v in tmp)
        {
            var key = NormalizeToken(v);
            if (!string.IsNullOrWhiteSpace(key))
            {
                if (aliases != null && aliases.TryGetValue(key, out var aliased))
                    set.Add(NormalizeToken(aliased));
                else
                    set.Add(key);
            }
        }
    }

    private void Truncate(HashSet<string> set, int max, string label)
    {
        if (set.Count <= max) return;
        _logger.LogWarning("{Label} exceeded cap {Max}; truncating from {Count}", label, max, set.Count);
        // Keep deterministic order by sorting then taking first N
        var cut = set.OrderBy(x => x, StringComparer.Ordinal).Take(max).ToArray();
        set.Clear();
        foreach (var v in cut) set.Add(v);
    }

    private static string CreateStamp(HashSet<string> roles, HashSet<string> perms)
    {
        var r = string.Join('|', roles.OrderBy(x => x, StringComparer.Ordinal));
        var p = string.Join('|', perms.OrderBy(x => x, StringComparer.Ordinal));
        return $"r:{r};p:{p}";
    }

    private static bool IsDevelopment()
        => string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
}
