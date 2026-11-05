using Koan.Mcp.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.Hosting;

/// <summary>What <c>initialize</c> tells a client, decided once for the handshake and for the console that shows it (AI-0042): the
/// application's <see cref="IMcpInstructions"/>, else <c>Koan:Mcp:Instructions</c>, else the application's description.</summary>
internal static class McpInstructions
{
    public static async Task<string?> Of(IServiceProvider? services, McpServerOptions options, CancellationToken ct)
    {
        if (services?.GetService<IMcpInstructions>() is { } provider) return await provider.Instructions(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(options.Instructions)) return options.Instructions;
        var description = Koan.Core.KoanEnv.CurrentSnapshot.Application.Description;
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }
}
