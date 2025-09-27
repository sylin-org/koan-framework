using System;
using System.IO;
using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using S13.DocMind.Contracts;
using S13.DocMind.Models;
using S13.DocMind.Services;
using Xunit;

namespace S13.DocMind.IntegrationTests;

public sealed class DocMindProcessingHarnessTests
{
    [Fact]
    public async Task Pipeline_CompletesDocument_WithHarnessFakes()
    {
        await using var harness = await DocMindTestHarness.StartAsync().ConfigureAwait(false);

        string documentId;
        using (var scope = harness.Services.CreateScope())
        {
            var intake = scope.ServiceProvider.GetRequiredService<IDocumentIntakeService>();
            var payload = Encoding.UTF8.GetBytes("DocMind harness coverage document.");
            var stream = new MemoryStream(payload, writable: false);
            var file = new FormFile(stream, 0, payload.Length, name: "file", fileName: "coverage.txt")
            {
                Headers = new HeaderDictionary(),
                ContentType = "text/plain"
            };

            var request = new UploadDocumentRequest
            {
                File = file,
                Description = "Harness upload"
            };

            var receipt = await intake.UploadAsync(request, CancellationToken.None).ConfigureAwait(false);
            documentId = receipt.DocumentId;
        }

        var workerHarness = new DocumentProcessingWorkerHarness(harness.Services, harness.Clock);
        for (var i = 0; i < 8; i++)
        {
            var processed = await workerHarness.ProcessBatchAsync(CancellationToken.None).ConfigureAwait(false);
            if (!processed)
            {
                break;
            }
        }

        var document = await SourceDocument.Get(documentId, CancellationToken.None).ConfigureAwait(false);
        document.Should().NotBeNull();
        document!.Status.Should().Be(DocumentProcessingStatus.Completed);
        document.Summary.LastCompletedStage.Should().Be(DocumentProcessingStage.Complete);
        document.Summary.InsightRefs.Should().NotBeEmpty();
        document.AssignedBySystem.Should().BeTrue();

        var job = await DocumentProcessingJobQueries.FindByDocumentAsync(Guid.Parse(documentId), CancellationToken.None).ConfigureAwait(false);
        job.Should().NotBeNull();
        job!.Status.Should().Be(DocumentProcessingStatus.Completed);
        job.Stage.Should().Be(DocumentProcessingStage.Complete);

        var events = await DocumentProcessingEvent.Query($"SourceDocumentId == '{documentId}'", CancellationToken.None).ConfigureAwait(false);
        events.Should().Contain(evt => evt.Stage == DocumentProcessingStage.GenerateInsights && evt.Metrics.Count > 0);
        events.Should().Contain(evt => evt.Stage == DocumentProcessingStage.Complete && evt.IsTerminal);

        harness.RefreshScheduler.RefreshCount.Should().BeGreaterThan(0);
    }
}
