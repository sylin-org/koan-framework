using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D8 — a pending Device Authorization Grant (RFC 8628). The row id is the ≥128-bit opaque
/// <c>device_code</c> (the device polls with it; <b>never logged</b>); <see cref="UserCode"/> is the short
/// human-typed code (stored normalized, rate-limited verification). On a separate device the user enters the
/// code and approves, which captures the consented subject identity (the device-poll has no session to read).
/// </summary>
public sealed class DeviceCode : Entity<DeviceCode>
{
    /// <summary>The normalized (dash-less, upper-case) user code — the verification lookup key.</summary>
    [Index]
    public string UserCode { get; set; } = "";

    public string ClientId { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Resource { get; set; } = "";

    /// <summary>pending | approved | denied.</summary>
    public string Status { get; set; } = StatusPending;

    // Captured on approval (the device-poll endpoint has no browser session).
    public string? Subject { get; set; }
    public string? SubjectName { get; set; }
    public string? SubjectEmail { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> GrantedScopes { get; set; } = new();

    public int IntervalSeconds { get; set; } = 5;
    public DateTimeOffset ExpiresUtc { get; set; }

    /// <summary>Last poll time — enforces the RFC 8628 minimum poll interval (slow_down).</summary>
    public DateTimeOffset? LastPolledUtc { get; set; }

    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusDenied = "denied";

    public bool IsPending => Status == StatusPending;
    public bool IsExpired(DateTimeOffset now) => ExpiresUtc <= now;
}
