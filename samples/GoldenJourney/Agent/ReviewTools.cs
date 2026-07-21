using Koan.GoldenJourney.Domain;
using Koan.GoldenJourney.Infrastructure;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Mcp;

namespace Koan.GoldenJourney.Agent;

public sealed class ReviewTools : Toolset
{
    [McpTool(
        Name = GoldenJourneyConstants.Agent.PendingTool,
        Description = "Lists a bounded page of assessed review requests that still need a recommendation.")]
    public Task<IReadOnlyList<ReviewRequest>> Pending(
        int limit = GoldenJourneyConstants.Agent.DefaultListSize,
        CancellationToken ct = default)
    {
        var bounded = Math.Clamp(limit, 1, GoldenJourneyConstants.Agent.MaximumListSize);
        return ReviewRequest.Query(
            request => request.Priority != ReviewPriority.Unassessed && request.Recommendation == null,
            new QueryDefinition { Page = 1, PageSize = bounded },
            ct);
    }

    [McpTool(
        Name = GoldenJourneyConstants.Agent.RecommendTool,
        Description = "Records a non-final recommendation for an assessed review request. A rationale is required.",
        IsMutation = true)]
    public async Task<RecommendationOutcome> Recommend(
        string id,
        ReviewDisposition disposition,
        string rationale,
        CancellationToken ct = default)
    {
        var request = await ReviewRequest.Get(id, ct);
        if (request is null)
            return new RecommendationOutcome(
                false,
                GoldenJourneyConstants.Outcomes.Missing,
                "No review request has that id.",
                id);

        var outcome = request.Recommend(disposition, rationale, "agent");
        if (outcome.Accepted) await request.Save(ct);
        return outcome;
    }
}
