using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Roles.Contracts;
using Koan.Web.Auth.Roles.Options;

namespace Koan.Web.Auth.Roles.Contributors;

/// <summary>
/// Applies an email-keyed role list to the principal: <c>allow</c> entries add roles,
/// <c>revoke</c> entries strip roles. Allow and revoke are explicit, separate operations —
/// removing an email from <c>allow</c> does NOT revoke (you must add to <c>revoke</c> to do that).
/// </summary>
/// <remarks>
/// File shape:
/// <code>
/// {
///   "allow":  { "user@example.com": ["admin", "curator"] },
///   "revoke": { "ex-admin@example.com": ["admin"] }
/// }
/// </code>
/// Singleton; safe under concurrent attribution. Caches the parsed file keyed on mtime + size,
/// re-stats at most every <see cref="RoleAttributionOptions.RoleListOptions.PollInterval"/>.
/// Missing file, parse errors, and missing claims are soft failures — the pipeline continues.
/// </remarks>
public sealed class RoleListContributor : IRoleMapContributor
{
    private readonly IOptionsMonitor<RoleAttributionOptions> _options;
    private readonly ILogger<RoleListContributor> _logger;

    private readonly object _gate = new();
    private string _cachedPath = "";
    private long _cachedSize = -1;
    private DateTime _cachedMtimeUtc = DateTime.MinValue;
    private DateTime _lastStatUtc = DateTime.MinValue;
    private RoleListFile _file = RoleListFile.Empty;

    public RoleListContributor(IOptionsMonitor<RoleAttributionOptions> options, ILogger<RoleListContributor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task Contribute(ClaimsPrincipal principal, ISet<string> roles, ISet<string> permissions, RoleAttributionContext? ctx, CancellationToken ct)
    {
        try
        {
            var cfg = _options.CurrentValue.RoleList;
            var path = cfg?.FilePath;
            if (string.IsNullOrWhiteSpace(path)) return Task.CompletedTask;

            var file = GetFile(path, cfg!.PollInterval);
            if (file.Allow.Count == 0 && file.Revoke.Count == 0) return Task.CompletedTask;

            foreach (var c in principal.FindAll(ClaimTypes.Email))
            {
                var email = c.Value;
                if (string.IsNullOrWhiteSpace(email)) continue;

                if (file.Allow.TryGetValue(email, out var grants) && grants is not null)
                {
                    foreach (var role in grants)
                    {
                        if (!string.IsNullOrWhiteSpace(role)) roles.Add(role);
                    }
                }
                if (file.Revoke.TryGetValue(email, out var revokes) && revokes is not null)
                {
                    foreach (var role in revokes)
                    {
                        if (!string.IsNullOrWhiteSpace(role)) roles.Remove(role);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Koan.Web.Auth.Roles: role list contributor failed; continuing without mutation");
        }
        return Task.CompletedTask;
    }

    private RoleListFile GetFile(string path, TimeSpan pollInterval)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var pathChanged = !string.Equals(path, _cachedPath, StringComparison.Ordinal);
            var dueForRecheck = pathChanged || (now - _lastStatUtc) >= pollInterval;
            if (!dueForRecheck) return _file;

            _lastStatUtc = now;

            FileInfo fi;
            try { fi = new FileInfo(path); }
            catch { return _file; }

            if (!fi.Exists)
            {
                if (pathChanged || _cachedSize != -1)
                {
                    _logger.LogWarning("Koan.Web.Auth.Roles: role list file not found at {Path}; grants disabled until file appears", path);
                }
                _cachedPath = path;
                _cachedSize = -1;
                _cachedMtimeUtc = DateTime.MinValue;
                _file = RoleListFile.Empty;
                return _file;
            }

            var size = fi.Length;
            var mtime = fi.LastWriteTimeUtc;
            if (!pathChanged && size == _cachedSize && mtime == _cachedMtimeUtc) return _file;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var parsed = JsonSerializer.Deserialize<RoleListDto>(stream, _jsonOpts) ?? new RoleListDto();
                _file = new RoleListFile(
                    new Dictionary<string, string[]>(parsed.Allow ?? new(), StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string[]>(parsed.Revoke ?? new(), StringComparer.OrdinalIgnoreCase));
                _cachedPath = path;
                _cachedSize = size;
                _cachedMtimeUtc = mtime;
                _logger.LogInformation(
                    "Koan.Web.Auth.Roles: role list loaded from {Path} (allow={Allow}, revoke={Revoke})",
                    path, _file.Allow.Count, _file.Revoke.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Koan.Web.Auth.Roles: failed to parse role list at {Path}; keeping previous file (allow={Allow}, revoke={Revoke})", path, _file.Allow.Count, _file.Revoke.Count);
            }
            return _file;
        }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record RoleListFile(Dictionary<string, string[]> Allow, Dictionary<string, string[]> Revoke)
    {
        public static readonly RoleListFile Empty = new(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class RoleListDto
    {
        [JsonPropertyName("allow")]
        public Dictionary<string, string[]>? Allow { get; set; }
        [JsonPropertyName("revoke")]
        public Dictionary<string, string[]>? Revoke { get; set; }
    }
}
