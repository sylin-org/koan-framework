using Microsoft.Extensions.Hosting;

namespace Koan.Mcp.CustomTools;

/// <summary>Stops the host at start over two tools of one name, or a <c>[Tool]</c> action a call could not reach (ARCH-0141): a client
/// sees one name, and only one of them would ever run.</summary>
internal sealed class McpToolNameCheck(McpEntityRegistry entities, McpCustomToolRegistry custom) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var duplicates = entities.Registrations.SelectMany(registration => registration.Tools.Select(tool => tool.Name))
            .Concat(custom.Tools.Select(tool => tool.Name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(names => names.Count() > 1)
            .Select(names => names.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"More than one MCP tool is named {string.Join(", ", duplicates)}. Give one a particle or a name with [Tool(...)], or rename the route.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
