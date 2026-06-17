using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Options;

namespace Koan.Web.Auth.Contributors.Builtin;

/// <summary>
/// Built-in <see cref="IKoanAuthFlowHandler"/> that applies an email-keyed allow/revoke role
/// list (loaded from a JSON file on disk) to the principal during sign-in. Replaces the previous
/// per-request <c>RoleListContributor</c> in the Roles module — semantics are unchanged for the
/// file shape; what changed is the trigger (sign-in event rather than every claims transformation).
/// </summary>
/// <remarks>
/// <para>
/// <b>Priority</b> is positive (50) so it runs AFTER application contributors that read
/// authoritative role state from a data store (e.g. a per-user <c>Roles</c> field). This makes
/// <c>revoke</c> work as documented — it can strip a role that an upstream contributor stamped.
/// </para>
/// <para>
/// Empty <c>FilePath</c> disables the contributor (default). Missing file, parse errors, and
/// missing email claims are soft failures — they log a warning and contribute nothing.
/// </para>
/// <para>
/// Singleton-like caching: the parsed file is cached by mtime + size, re-stat'd at the configured
/// poll interval at most. Multiple concurrent sign-ins share a single parse.
/// </para>
/// </remarks>
public sealed class RoleListFileContributor : IKoanAuthFlowHandler
{
    public int Priority => 50;

    private readonly IOptionsMonitor<AuthLifecycleOptions> _options;
    private readonly ILogger<RoleListFileContributor> _logger;

    // mtime-based cache shared across requests. Lock-guarded to keep the I/O path single-flight
    // while the in-flight check itself remains cheap.
    private readonly object _gate = new();
    private string _cachedPath = "";
    private long _cachedSize = -1;
    private DateTime _cachedMtimeUtc = DateTime.MinValue;
    private DateTime _lastStatUtc = DateTime.MinValue;
    private RoleListFile _file = RoleListFile.Empty;

    public RoleListFileContributor(IOptionsMonitor<AuthLifecycleOptions> options, ILogger<RoleListFileContributor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        var cfg = _options.CurrentValue.RoleListFile;
        var path = cfg?.FilePath;
        if (string.IsNullOrWhiteSpace(path)) return Task.CompletedTask;

        try
        {
            var file = GetFile(path, cfg!.PollInterval);
            if (file.Allow.Count == 0 && file.Revoke.Count == 0) return Task.CompletedTask;

            // Email is the lookup key. We read it from the identity being signed in — this is the
            // post-mapping identity, so it reflects whatever upstream contributors stamped, plus
            // the email claim that the OAuth provider asserted.
            foreach (var c in ctx.Identity.FindAll(ClaimTypes.Email))
            {
                var email = c.Value;
                if (string.IsNullOrWhiteSpace(email)) continue;

                if (file.Allow.TryGetValue(email, out var grants) && grants is not null)
                {
                    foreach (var role in grants)
                    {
                        if (string.IsNullOrWhiteSpace(role)) continue;
                        if (!ctx.Identity.HasClaim(ClaimTypes.Role, role))
                            ctx.Identity.AddClaim(new Claim(ClaimTypes.Role, role));
                    }
                }
                if (file.Revoke.TryGetValue(email, out var revokes) && revokes is not null)
                {
                    foreach (var role in revokes)
                    {
                        if (string.IsNullOrWhiteSpace(role)) continue;
                        foreach (var match in ctx.Identity.FindAll(ClaimTypes.Role).Where(rc => string.Equals(rc.Value, role, StringComparison.OrdinalIgnoreCase)).ToArray())
                            ctx.Identity.TryRemoveClaim(match);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Koan.Web.Auth: RoleListFileContributor failed for {UserId} with path {Path}; continuing pipeline",
                ctx.UserId, path);
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
                    _logger.LogWarning(
                        "Koan.Web.Auth: role list file not found at {Path}; allow/revoke disabled until file appears",
                        path);
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
                    "Koan.Web.Auth: role list loaded from {Path} (allow={Allow}, revoke={Revoke})",
                    path, _file.Allow.Count, _file.Revoke.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth: failed to parse role list at {Path}; keeping previous file (allow={Allow}, revoke={Revoke})",
                    path, _file.Allow.Count, _file.Revoke.Count);
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
