using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Media.Core;
using Sora.Media.Core.Extensions;
using Sora.Media.Core.Model;
using Sora.Storage;
using Sora.Storage.Abstractions;
using Sora.Storage.Extensions;
using Sora.Storage.Local;
using Xunit;

namespace Sora.Media.Tests;

public class MediaBasicsTests
{
    private static IServiceProvider BuildServices(string basePath)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Sora:Storage:Profiles:default:Provider"] = "local",
            ["Sora:Storage:Profiles:default:Container"] = "media",
            ["Sora:Storage:DefaultProfile"] = "default",
            ["Sora:Storage:Providers:Local:BasePath"] = basePath
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        var services = new ServiceCollection();
        services.AddSoraStorage(config);
        services.AddSoraLocalStorageProvider(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Upload_Read_And_Delete_Work()
    {
        using var temp = new TempFolder();
    var sp = BuildServices(temp.Path);
    Sora.Core.Hosting.App.AppHost.Current = sp;
        var storage = sp.GetRequiredService<IStorageService>();

        // Upload via MediaEntity first-class API
        await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello media"));
        var media = await ProfileMedia.Upload(content, name: "hello.txt", contentType: "text/plain");
        media.Provider.Should().Be("local");
        media.Container.Should().Be("media");

        // OpenRead via static
        await using (var read = await ProfileMedia.OpenRead(media.Key))
        using (var sr = new StreamReader(read))
        {
            (await sr.ReadToEndAsync()).Should().Be("hello media");
        }

        // Url helper should throw (local provider has no presign) — ensure it fails predictably for now
        Func<Task> urlCall = async () => { _ = await media.Url(); };
        await urlCall.Should().ThrowAsync<NotSupportedException>();

        // Cleanup
        (await storage.DeleteAsync("default", "", media.Key)).Should().BeTrue();
    }

    [Fact]
    public async Task Copy_And_Move_Between_Profiles_Work()
    {
        using var temp = new TempFolder();
        var dict = new Dictionary<string, string?>
        {
            ["Sora:Storage:Profiles:hot:Provider"] = "local",
            ["Sora:Storage:Profiles:hot:Container"] = "hot",
            ["Sora:Storage:Profiles:cold:Provider"] = "local",
            ["Sora:Storage:Profiles:cold:Container"] = "cold",
            ["Sora:Storage:DefaultProfile"] = "hot",
            ["Sora:Storage:Providers:Local:BasePath"] = temp.Path
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        var services = new ServiceCollection();
        services.AddSoraStorage(config);
        services.AddSoraLocalStorageProvider(config);
    var sp = services.BuildServiceProvider();
    Sora.Core.Hosting.App.AppHost.Current = sp;

        var storage = sp.GetRequiredService<IStorageService>();

        // Use ProfileMedia but bound to 'hot' by attribute override for this test
        // For simplicity, create directly via storage then exercise model transfer helpers
        await storage.CreateTextFile("file.txt", "x", profile: "hot");
        var proxy = Sora.Media.Abstractions.Model.MediaEntity<ProfileMedia>.Get("file.txt");
        var copied = await proxy.CopyTo<ColdProfileMedia>();
        copied.Container.Should().Be("cold");
        (await storage.ExistsAsync("cold", "cold", "file.txt")).Should().BeTrue();

        var moved = await proxy.MoveTo<ColdProfileMedia>();
        moved.Container.Should().Be("cold");
        (await storage.ExistsAsync("hot", "hot", "file.txt")).Should().BeFalse();
        (await storage.ExistsAsync("cold", "cold", "file.txt")).Should().BeTrue();

        await storage.EnsureDeleted("cold", "cold", "file.txt");
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sora-media-tests-" + Guid.NewGuid().ToString("N"));
        public TempFolder() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
