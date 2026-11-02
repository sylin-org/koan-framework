using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace Koan.Mcp.CustomTools;

/// <summary>A tool call reaches the action it named and no other (ARCH-0141): routing still matches its address and constraints, among that action alone.</summary>
internal sealed class NamedActionPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => 0;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
        => endpoints.Any(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null);

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        if (httpContext.Features.Get<RouteTool>() is { } tool)
            for (var i = 0; i < candidates.Count; i++)
                if (candidates[i].Endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.Id != tool.Action.Id)
                    candidates.SetValidity(i, false);
        return Task.CompletedTask;
    }
}
