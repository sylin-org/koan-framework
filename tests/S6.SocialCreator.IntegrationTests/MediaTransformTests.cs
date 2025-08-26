using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SixLabors.ImageSharp;
using Sora.Core;
using Xunit;

namespace S6.SocialCreator.IntegrationTests;

public sealed class MediaTransformTests
{
    [Fact]
    public async Task Image_request_with_query_redirects_to_canonical_variant_and_serves_transformed_bytes()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Sora:Storage:Profiles:local:Provider"] = "local",
                    ["Sora:Storage:Profiles:local:Container"] = "media",
                    ["Sora:Storage:Profiles:default:Provider"] = "local",
                    ["Sora:Storage:Profiles:default:Container"] = "media",
                    ["Sora:Storage:DefaultProfile"] = "local",
                    ["Sora:Storage:Providers:local:BasePath"] = Path.Combine(Path.GetTempPath(), "sora-s6-tests")
                };
                cfg.AddInMemoryCollection(dict!);
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IHostedService>(sp => new AppHostHostedService(sp));
            });
        });

    var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Prepare a tiny 1x1 PNG (white) as upload bytes (generated to ensure validity)
        byte[] pngBytes;
        using (var img = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1))
        {
            img[0, 0] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 255, 255);
            using var ms = new MemoryStream();
            await img.SaveAsPngAsync(ms);
            pngBytes = ms.ToArray();
        }

        var mp = new MultipartFormDataContent();
        var file = new ByteArrayContent(pngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        mp.Add(file, "file", "one.png");
        var upload = await client.PostAsync("/api/upload", mp);
        upload.EnsureSuccessStatusCode();
        var id = System.Text.Json.JsonDocument.Parse(await upload.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // Request with transform query -> should 301 to canonical variant id
        var resp = await client.GetAsync($"/api/media/{id}/one.png?w=2&format=webp");
        resp.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.TryGetValues("X-Media-Variant", out var variantHeaders).Should().BeTrue();
        variantHeaders!.First().Should().NotBeNullOrWhiteSpace();

        // Follow redirect
        var redirected = await client.GetAsync(resp.Headers.Location);
        redirected.StatusCode.Should().Be(HttpStatusCode.OK);
        redirected.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        var bytes = await redirected.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
    }

    private sealed class AppHostHostedService : IHostedService
    {
        private readonly IServiceProvider _sp;
        public AppHostHostedService(IServiceProvider sp) => _sp = sp;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Sora.Core.Hosting.App.AppHost.Current = _sp;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (ReferenceEquals(Sora.Core.Hosting.App.AppHost.Current, _sp)) Sora.Core.Hosting.App.AppHost.Current = null;
            return Task.CompletedTask;
        }
    }
}
