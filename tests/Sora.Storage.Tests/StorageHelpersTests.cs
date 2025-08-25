using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Storage;
using Sora.Storage.Local;
using System.Net;
using System.Net.Http;
using Xunit;

public class StorageHelpersTests
{
    private static IServiceProvider BuildServices(string basePath)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Sora:Storage:Profiles:default:Provider"] = "local",
            ["Sora:Storage:Profiles:default:Container"] = "test",
            ["Sora:Storage:Providers:Local:BasePath"] = basePath
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        var services = new ServiceCollection();
        services.AddSoraStorage(config);
        services.AddSoraLocalStorageProvider(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateText_ReadAllText_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        var obj = await svc.CreateTextFile("foo/bar.txt", "hello", profile: "default");
        obj.ContentType.Should().StartWith("text/plain");
        (await svc.ReadAllText("default", "", "foo/bar.txt")).Should().Be("hello");
        (await svc.TryDelete("default", "", "foo/bar.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task CreateJson_Generates_Json_ContentType()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        var obj = await svc.CreateJson("data/item.json", new { a = 1, b = "x" }, profile: "default");
        obj.ContentType.Should().StartWith("application/json");
        var text = await svc.ReadAllText("default", "", "data/item.json");
        text.Should().Contain("\"a\":1");
        await svc.EnsureDeleted("default", "", "data/item.json");
    }

    [Fact]
    public async Task InProfile_Chaining_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        var p = svc.InProfile("default");
        await p.CreateTextFile("nest/hi.txt", "hi");
        (await svc.ReadAllText("default", "", "nest/hi.txt")).Should().Be("hi");
        await svc.EnsureDeleted("default", "", "nest/hi.txt");
    }

    [Fact]
    public async Task OnboardFile_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        var tmpFile = Path.Combine(temp.Path, "source.txt");
        await File.WriteAllTextAsync(tmpFile, "from-file");

        var obj = await svc.OnboardFile(tmpFile, key: "uploads/source.txt", profile: "default");
        obj.Key.Should().Be("uploads/source.txt");
        (await svc.ReadAllText("default", "", "uploads/source.txt")).Should().Be("from-file");
        await svc.EnsureDeleted("default", "", "uploads/source.txt");
    }

    [Fact]
    public async Task OnboardUrl_Works_With_Custom_HttpClient()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        var handler = new FakeHandler("from-web", contentType: "text/plain; charset=utf-8");
        using var http = new HttpClient(handler);

        var uri = new Uri("http://test.local/web.txt");
        var obj = await svc.OnboardUrl(uri, http: http, profile: "default");
        obj.Key.Should().EndWith("web.txt");
        (await svc.ReadAllText("default", "", obj.Key!)).Should().Be("from-web");
        await svc.EnsureDeleted("default", "", obj.Key!);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _payload;
        private readonly string _contentType;
        public FakeHandler(string payload, string contentType)
        {
            _payload = payload;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_payload)
            };
            msg.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(_contentType);
            return Task.FromResult(msg);
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sora-storage-tests-" + Guid.NewGuid().ToString("N"));
        public TempFolder() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
