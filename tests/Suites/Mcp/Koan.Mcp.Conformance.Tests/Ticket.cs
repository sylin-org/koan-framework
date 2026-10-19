using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Web.Hooks;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// WEB-0074: an entity exposed over MCP whose application gives <c>q</c> a meaning the supported way - a
/// request-options hook that turns the text into a typed filter - so the specs can show that is the only path
/// <c>q</c> takes to the data layer.
/// </summary>
[McpEntity(Name = "ticket", Description = "A support ticket", Exposure = McpExposureMode.Full)]
[StorageName("nk007_tickets")]
public sealed class Ticket : Entity<Ticket>
{
    public string Title { get; set; } = "";
}

/// <summary>Maps <c>q</c> to "Title contains" and clears it, so nothing is left for the endpoint to refuse.</summary>
internal sealed class TicketTitleSearch : IRequestOptionsHook<Ticket>
{
    public int Order => 0;

    public Task OnBuildingOptions(HookContext<Ticket> context, QueryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Q)) return Task.CompletedTask;
        var term = options.Q!;
        options.AddPredicate<Ticket>(ticket => ticket.Title.Contains(term));
        options.Q = null;
        return Task.CompletedTask;
    }
}
