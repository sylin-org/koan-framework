using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Storage;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace S6.SocialCreator.IntegrationTests;

public sealed class S6SmokeTests
{
    [Fact]
    public async Task Home_and_Upload_flow_Works()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                // Minimal local storage profile for tests
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
                // Ensure AppHost.Current during tests
                services.AddSingleton<IHostedService>(sp => new AppHostHostedService(sp));
            });
        });

        var client = app.CreateClient();

        // Home (serves unified shell)
        var home = await client.GetAsync("/");
        home.StatusCode.Should().Be(HttpStatusCode.OK);

        // Post upload (small text as file)
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hi"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "hi.txt");
        var upload = await client.PostAsync("/api/upload", content);
        upload.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted, HttpStatusCode.Created);

        // Extract key from JSON
        var body = await upload.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
    var key = JToken.Parse(body)["key"]!.Value<string>();
        key.Should().NotBeNull();

        // HEAD
        var headReq = new HttpRequestMessage(HttpMethod.Head, $"/api/media/{key}");
        var head = await client.SendAsync(headReq);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

        // GET full
        var get = await client.GetAsync($"/api/media/{key}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var txt = await get.Content.ReadAsStringAsync();
        txt.Should().Be("hi");

        // Range GET
        var rangeReq = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{key}");
        rangeReq.Headers.Range = new RangeHeaderValue(0, 0); // first byte
        var range = await client.SendAsync(rangeReq);
        range.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var slice = await range.Content.ReadAsByteArrayAsync();
        slice.Length.Should().Be(1);
    }

    [Fact]
    public async Task Caching_and_Range_Validation_Works()
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

        var client = app.CreateClient();

        // Upload a tiny file
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hi"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "hi.txt");
        var upload = await client.PostAsync("/api/upload", content);
        upload.EnsureSuccessStatusCode();
    var key = Newtonsoft.Json.Linq.JToken.Parse(await upload.Content.ReadAsStringAsync())["key"]?.ToString();
        key.Should().NotBeNull();

        // Initial GET to capture Last-Modified
        var initial = await client.GetAsync($"/api/media/{key}");
        initial.StatusCode.Should().Be(HttpStatusCode.OK);
        var lastModified = initial.Content.Headers.LastModified;
        lastModified.HasValue.Should().BeTrue();

        // Conditional GET with If-Modified-Since should 304
        var cond = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{key}");
        cond.Headers.IfModifiedSince = lastModified;
        var notModified = await client.SendAsync(cond);
        notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);

        // If-None-Match using ETag should also 304
        var etag = initial.Headers.ETag;
        etag.Should().NotBeNull("ETag should be set on the response headers");
        var inmReq = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{key}");
        inmReq.Headers.TryAddWithoutValidation("If-None-Match", etag!.ToString());
        var inmResp = await client.SendAsync(inmReq);
        inmResp.StatusCode.Should().Be(HttpStatusCode.NotModified);

        // Mismatched ETag should return 200
        var missReq = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{key}");
        missReq.Headers.TryAddWithoutValidation("If-None-Match", "\"bogus\"");
        var missResp = await client.SendAsync(missReq);
        missResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Invalid range should 416 with Content-Range: bytes */length
        var invalid = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{key}");
        invalid.Headers.Range = new RangeHeaderValue(999, null);
        var invalidResp = await client.SendAsync(invalid);
        invalidResp.StatusCode.Should().Be((HttpStatusCode)416);
        IEnumerable<string>? crVals;
        var hasCr = invalidResp.Headers.TryGetValues("Content-Range", out crVals) || (invalidResp.Content?.Headers?.TryGetValues("Content-Range", out crVals) ?? false);
        hasCr.Should().BeTrue();
        crVals!.First().Should().MatchRegex(@"^bytes \*/\d+$");
        invalidResp.Headers.TryGetValues("Accept-Ranges", out var ar).Should().BeTrue();
        ar!.First().Should().Be("bytes");

        // Suffix range for last 1 byte should 206 and length 1
        var suffix = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{key}");
        suffix.Headers.Add("Range", "bytes=-1");
        var suffixResp = await client.SendAsync(suffix);
        suffixResp.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var lastByte = await suffixResp.Content.ReadAsByteArrayAsync();
        lastByte.Length.Should().Be(1);
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
