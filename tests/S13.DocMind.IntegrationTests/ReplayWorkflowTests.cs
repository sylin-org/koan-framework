using FluentAssertions;
using S13.DocMind.Tools;
using S13.DocMind.Services;
using Xunit;

namespace S13.DocMind.IntegrationTests;

public class ReplayWorkflowTests
{
    [Fact(Skip = "Requires running DocMind API and vector infrastructure")]
    public async Task ReplayEndpointAcceptsStageOverrides()
    {
        using var client = new DocMindProcessingClient("http://localhost:5113");
        var request = new ProcessingReplayRequest
        {
            DocumentId = Guid.NewGuid().ToString(),
            Stage = DocumentProcessingStage.GenerateEmbeddings,
            Reset = true
        };

        var result = await client.ReplayAsync(request, CancellationToken.None).ConfigureAwait(false);
        result.Should().NotBeNull();
    }
}
