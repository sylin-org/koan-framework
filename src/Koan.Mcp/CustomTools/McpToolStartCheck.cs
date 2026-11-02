using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;

namespace Koan.Mcp.CustomTools;

/// <summary>Stops the host at start over what could never work (ARCH-0141). Two tools of one name: a client sees one name, and only one of them would ever run. A tool whose ASP.NET
/// policy names an authentication scheme: its evaluator authenticates that scheme again, from a credential a tool call does not carry, and the caller would be refused.</summary>
internal sealed class McpToolStartCheck(McpEntityRegistry entities, McpCustomToolRegistry custom, IAuthorizationPolicyProvider? policies = null) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var duplicates = entities.Registrations.SelectMany(registration => registration.Tools.Select(tool => tool.Name))
            .Concat(custom.Tools.Select(tool => tool.Name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(names => names.Count() > 1)
            .Select(names => names.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"More than one MCP tool is named {string.Join(", ", duplicates)}. Give one a particle or a name with [Tool(...)], or rename the route.");

        if (policies is null) return;
        foreach (var tool in custom.Tools)
            if (tool.Route is not null && await tool.Route.Policy(policies).ConfigureAwait(false) is { AuthenticationSchemes.Count: > 0 } policy)
                throw new InvalidOperationException($"Tool {tool.Name} answers to a policy that names the authentication scheme {string.Join(", ", policy.AuthenticationSchemes)}, whose credential a tool call does not carry. Remove the scheme from the policy, or move the action to a controller without [Tool].");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
