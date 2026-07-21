using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Web.Authorization;

namespace Koan.Mcp.Operations;

/// <summary>Raised by an operational verb when the caller is not authorized — surfaced to the agent as a tool error
/// naming the exact <c>@ops:{key}</c> grant it needs (the card's "fail-loud" contract).</summary>
internal sealed class McpOpsDeniedException : Exception
{
    public McpOpsDeniedException(string message) : base(message) { }
}

/// <summary>
/// P3.2 — the shared runtime gate for operational MCP verbs: the <c>@ops:{key}</c> grant requirement, the
/// confirm-or-dry-run contract for destructive verbs, and the <see cref="AgentAction"/> audit on every mutation.
/// Reuses SEC-0005's grant + audit entities directly (no parallel machinery). The config-visibility gate is enforced
/// upstream by <c>McpToolAccessPolicy</c> via <c>[McpOperationalToolset]</c> — this gate is the runtime authority.
/// </summary>
internal static class OpsGate
{
    /// <summary>The <c>@ops:</c> namespace prefix for an operational grant/audit resource.</summary>
    public static string Resource(string opsKey) => "@ops:" + opsKey;

    /// <summary>Require an active <c>@ops:{opsKey}</c> grant for the caller; return the subject, or throw a
    /// descriptive denial. The grant must match the EXACT operational resource — a blanket <c>"*"</c> entity grant
    /// does NOT confer ops (operational authority is explicit). An anonymous caller (STDIO/local) has no subject and
    /// therefore cannot hold an ops grant.</summary>
    public static async Task<string> RequireGrant(ClaimsPrincipal? principal, string opsKey, CancellationToken ct = default)
    {
        var resource = Resource(opsKey);
        var subject = Subject(principal);
        if (string.IsNullOrEmpty(subject))
        {
            throw new McpOpsDeniedException(
                $"Forbidden: the '{resource}' operational verbs require an AgentGrant, but the caller is anonymous " +
                $"(no subject — e.g. a STDIO/local call). A governed remote agent must hold an active '{resource}' grant.");
        }

        var now = DateTimeOffset.UtcNow;
        var grants = await AgentGrant.Query(g => g.Subject == subject && g.Resource == resource).ConfigureAwait(false);
        if (!grants.Any(g => g.IsActive(now)))
        {
            throw new McpOpsDeniedException(
                $"Forbidden: subject '{subject}' lacks an active '{resource}' AgentGrant. " +
                $"Issue one with:  new AgentGrant {{ Subject = \"{subject}\", Resource = \"{resource}\" }}.Save();");
        }

        return subject;
    }

    /// <summary>The dry-run refusal a destructive verb returns when <c>confirm</c> is not set (the default).</summary>
    public static string DryRun(string wouldDo)
        => $"DRY RUN — no changes made. This WOULD: {wouldDo}. Call again with \"confirm\": true to execute.";

    /// <summary>Record one mutation in the SEC-0005 audit trail.</summary>
    public static Task Audit(string subject, string opsKey, string action, string entityId)
        => new AgentAction
        {
            Subject = subject,
            Resource = Resource(opsKey),
            Action = action,
            EntityId = entityId,
            At = DateTimeOffset.UtcNow,
        }.Save();

    private static string Subject(ClaimsPrincipal? p)
        => p?.FindFirst("sub")?.Value ?? p?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
}
