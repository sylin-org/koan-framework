using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Koan.Data.Access;
using Koan.Jobs;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SnapVault.Models;
using Xunit;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>Proves the public local-first path through SnapVault's real HTTP, Jobs, Data, Storage, and Media surfaces.</summary>
[Collection("snapvault")]
public sealed class SnapVaultGoldenPathSpec(SnapVaultHostFixture fixture)
{
    [Fact]
    public async Task Fresh_development_host_accepts_and_serves_one_photo_without_external_services()
    {
        using var client = fixture.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        (await client.GetAsync("/health/ready", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var fileName = $"meaningful-{Guid.NewGuid():N}.jpg";
        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(await TinyJpegAsync());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        upload.Add(file, "files", fileName);

        var accepted = await client.PostAsync("/api/photos/upload", upload, ct);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        using var acceptance = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(ct));
        acceptance.RootElement.GetProperty("totalQueued").GetInt64().Should().Be(1);
        acceptance.RootElement.GetProperty("totalFailed").GetInt32().Should().Be(0);
        var batchId = acceptance.RootElement.GetProperty("jobId").GetString()!;

        var orchestrator = fixture.Host.Services.GetRequiredService<JobOrchestrator>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await orchestrator.DrainAsync(ct);
            using (Tenant.Use(TenancyDevSeed.DevTenantId))
            {
                var work = await PhotoProcessingJob.Query(job => job.BatchJobId == batchId, ct);
                if (work.Count == 1 && await PhotoProcessingJob.Jobs.Status(work[0].Id, ct) == JobStatus.Completed)
                    break;
            }
        }

        PhotoAsset photo;
        using (Tenant.Use(TenancyDevSeed.DevTenantId))
        using (Subject.System())
        {
            var matches = await PhotoAsset.Query(candidate => candidate.OriginalFileName == fileName, ct);
            photo = matches.Should().ContainSingle().Subject;
            photo.Width.Should().Be(8);
            photo.Height.Should().Be(6);
            (await Event.Get(photo.EventId, ct)).Should().NotBeNull();
        }

        var original = await client.GetAsync($"/media/{photo.Id}", ct);
        original.StatusCode.Should().Be(HttpStatusCode.OK);
        original.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await original.Content.ReadAsByteArrayAsync(ct)).Should().NotBeEmpty();

        var gallery = await client.GetAsync($"/media/{photo.Id}/gallery", ct);
        gallery.StatusCode.Should().Be(HttpStatusCode.OK);
        gallery.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");

        (await client.GetAsync("/health/ready", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var facts = fixture.Host.Services.GetRequiredService<IKoanRuntimeFacts>().Current;
        facts.Complete.Should().BeTrue();
        facts.Facts.Should().Contain(fact =>
            fact.Subject == "data:default"
            && fact.Summary.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
        facts.Facts.Should().NotContain(fact =>
            fact.Subject.Contains("Vector.Connector", StringComparison.OrdinalIgnoreCase)
            && fact.State == KoanFactState.Selected);
    }

    private static async Task<byte[]> TinyJpegAsync()
    {
        using var image = new Image<Rgba32>(8, 6);
        using var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream);
        return stream.ToArray();
    }
}
