using System;

namespace Koan.Mcp;

/// <summary>
/// P3.2 — marks a <see cref="Toolset"/> as an OPERATIONAL toolset gated by config: its <c>[McpTool]</c> verbs are
/// visible in <c>tools/list</c> and invocable ONLY when <c>Koan:Mcp:Operations:{Key}</c> is enabled (all default
/// OFF — operational verbs are privileged). The runtime grant gate (the verb's <c>@ops:{Key}</c>
/// <see cref="Koan.Web.Authorization.AgentGrant"/> requirement) is enforced separately, inside the verb, so a
/// config-enabled toolset is still discoverable-but-denied without the grant (the agent learns what it needs).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpOperationalToolsetAttribute : Attribute
{
    public McpOperationalToolsetAttribute(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("An operational toolset key is required.", nameof(key));
        Key = key.Trim();
    }

    /// <summary>The toolset key — the <c>Koan:Mcp:Operations:{Key}</c> config flag and the <c>@ops:{Key}</c> grant namespace.</summary>
    public string Key { get; }
}
