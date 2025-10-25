using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Samples.Meridian.Contracts;
using Xunit;

namespace S7.Meridian.Tests;

public sealed class AiSuggestEndpointTests : IClassFixture<MeridianWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AiSuggestEndpointTests(MeridianWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SourceTypeAiSuggest_GeneratesDraftFromSeed()
    {
        var request = new SourceTypeAiSuggestRequest
        {
            SeedText = """
                Meeting Notes:
                - Vendor highlighted annual revenue of $47.2M and 12% YoY growth.
                - Headcount reported at 150 with a dedicated compliance team.
                - Document references SOC 2 controls and FedRAMP moderate roadmap.
                """,
            DocumentName = "enterprise-architecture-notes.txt",
            AdditionalContext = "Focus on financial stability indicators and compliance posture.",
            TargetFields = new List<string> { "$.annualRevenue", "$.headcount" },
            DesiredTags = new List<string> { "finance", "architecture" }
        };

        var response = await _client.PostAsJsonAsync("/api/sourcetypes/ai-suggest", request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SourceTypeAiSuggestResponse>();
        payload.Should().NotBeNull();
        payload!.Draft.Name.Should().NotBeNullOrWhiteSpace();
        payload.Draft.Description.Should().NotBeNullOrWhiteSpace();
        payload.Draft.Instructions.Should().NotBeNullOrWhiteSpace();
        payload.Draft.OutputTemplate.Should().NotBeNullOrWhiteSpace();
        payload.Draft.FieldQueries.Should().NotBeNull();
        payload.Draft.FieldQueries.Count.Should().BeGreaterOrEqualTo(0);
        payload.Warnings.Should().NotBeNull();
    }

    [Fact]
    public async Task AnalysisTypeAiSuggest_GeneratesNarrativeTemplate()
    {
        var request = new AnalysisTypeAiSuggestRequest
        {
            Prompt = "Produce an enterprise architecture review summarizing strengths, risks, and recommended actions for a CIO steering committee. Highlight integration risks, cybersecurity posture, and vendor roadmap alignment."
        };

        var response = await _client.PostAsJsonAsync("/api/analysistypes/ai-suggest", request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AnalysisTypeAiSuggestResponse>();
        payload.Should().NotBeNull();
        payload!.Draft.Name.Should().NotBeNullOrWhiteSpace();
        payload.Draft.Description.Should().NotBeNullOrWhiteSpace();
        payload.Draft.Instructions.Should().NotBeNullOrWhiteSpace();
        payload.Draft.OutputTemplate.Should().NotBeNullOrWhiteSpace();
        payload.Warnings.Should().NotBeNull();
    }
}
