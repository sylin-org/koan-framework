using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SixLabors.ImageSharp;
using Koan.Core;
using System.Net;
using System.Net.Http.Headers;
using Xunit;
using Newtonsoft.Json.Linq;

namespace S6.SocialCreator.IntegrationTests;

public sealed class ImageSanityTests
{
    [Fact]
    public async Task Uploaded_png_is_decodable_via_direct_get()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Koan:Storage:Profiles:local:Provider"] = "local",
                    ["Koan:Storage:Profiles:local:Container"] = "media",
                    ["Koan:Storage:Profiles:default:Provider"] = "local",
                    ["Koan:Storage:Profiles:default:Container"] = "media",
                    ["Koan:Storage:DefaultProfile"] = "local",
                    ["Koan:Storage:Providers:local:BasePath"] = Path.Combine(Path.GetTempPath(), "Koan-s6-tests")
                };
                cfg.AddInMemoryCollection(dict!);
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IHostedService>(sp => new AppHostHostedService(sp));
            });
        });

        var client = app.CreateClient();

        // Generate a tiny 1x1 PNG (white) using ImageSharp to ensure validity
        byte[] pngBytes;
        using (var img = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1))
        {
            img[0, 0] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 255, 255);
            using var msGen = new MemoryStream();
            await img.SaveAsPngAsync(msGen);
            pngBytes = msGen.ToArray();
        }
        var mp = new MultipartFormDataContent();
        var file = new ByteArrayContent(pngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        mp.Add(file, "file", "one.png");
        var upload = await client.PostAsync("/api/upload", mp);
        upload.EnsureSuccessStatusCode();
    var key = JToken.Parse(await upload.Content.ReadAsStringAsync())["key"]!.Value<string>();

        var get = await client.GetAsync($"/api/media/{key}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var bytes = await get.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
        bytes.SequenceEqual(pngBytes).Should().BeTrue("downloaded bytes should match uploaded bytes");

        using var decoded = Image.Load(bytes);
        decoded.Width.Should().BeGreaterThan(0);
        decoded.Height.Should().BeGreaterThan(0);
    }

    private sealed class AppHostHostedService : IHostedService
    {
        private readonly IServiceProvider _sp;
        public AppHostHostedService(IServiceProvider sp) => _sp = sp;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Koan.Core.Hosting.App.AppHost.Current = _sp;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (ReferenceEquals(Koan.Core.Hosting.App.AppHost.Current, _sp)) Koan.Core.Hosting.App.AppHost.Current = null;
            return Task.CompletedTask;
        }
    }
}
