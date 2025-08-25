using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Storage;
using Sora.Storage.Local;
using Sora.Storage.Options;
using Xunit;

public class LocalProviderTests
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
    public async Task Write_Read_Delete_Range_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world"));
    var obj = await svc.PutAsync("default", "", "folder1/hello.txt", content, "text/plain");
        obj.Provider.Should().Be("local");
        obj.Container.Should().Be("test");
    obj.ContentHash.Should().Be("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");

        await using (var read = await svc.ReadAsync("default", "", "folder1/hello.txt"))
        {
            using var sr = new StreamReader(read);
            (await sr.ReadToEndAsync()).Should().Be("hello world");
        }

        var (rangeStream, len) = await svc.ReadRangeAsync("default", "", "folder1/hello.txt", 6, 10);
        using var sr2 = new StreamReader(rangeStream);
        (await sr2.ReadToEndAsync()).Should().Be("world");
        len.Should().Be(5);

        var deleted = await svc.DeleteAsync("default", "", "folder1/hello.txt");
        deleted.Should().BeTrue();
    }

    [Fact]
    public async Task Exists_Head_Work()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        (await svc.ExistsAsync("default", "", "a/b.txt")).Should().BeFalse();
        await svc.CreateTextFile("a/b.txt", "hi", profile: "default");
        (await svc.ExistsAsync("default", "", "a/b.txt")).Should().BeTrue();
        var head = await svc.HeadAsync("default", "", "a/b.txt");
        head.Should().NotBeNull();
        head!.Length.Should().Be(2);
        await svc.EnsureDeleted("default", "", "a/b.txt");
    }

    [Fact]
    public async Task TransferToProfile_Works_With_ServerSideCopy()
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

        var svc = sp.GetRequiredService<IStorageService>();
        await svc.CreateTextFile("doc.txt", new string('x', 10), profile: "hot");

        // Transfer same key from hot->cold; local provider supports server-side copy
        var transferred = await svc.TransferToProfileAsync("hot", "", "doc.txt", "cold");
        transferred.Provider.Should().Be("local");
        transferred.Container.Should().Be("cold");
        (await svc.ExistsAsync("cold", "", "doc.txt")).Should().BeTrue();

        // Cleanup
        await svc.EnsureDeleted("hot", "", "doc.txt");
        await svc.EnsureDeleted("cold", "", "doc.txt");
    }

    [Fact]
    public async Task CopyTo_And_MoveTo_Work()
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

        var svc = sp.GetRequiredService<IStorageService>();

        await svc.CreateTextFile("copyme.txt", "copy", profile: "hot");

    // CopyTo (deleteSource=false)
    await svc.CopyTo("hot", "", "copyme.txt", "cold");
        (await svc.ExistsAsync("hot", "", "copyme.txt")).Should().BeTrue();
        (await svc.ExistsAsync("cold", "", "copyme.txt")).Should().BeTrue();

    // MoveTo (deleteSource=true)
    await svc.MoveTo("hot", "", "copyme.txt", "cold");
        (await svc.ExistsAsync("hot", "", "copyme.txt")).Should().BeFalse();
        (await svc.ExistsAsync("cold", "", "copyme.txt")).Should().BeTrue();

        // Cleanup
        await svc.EnsureDeleted("hot", "", "copyme.txt");
        await svc.EnsureDeleted("cold", "", "copyme.txt");
    }

    [Fact]
    public async Task DefaultProfile_Is_Used_When_Profile_Not_Specified()
    {
        using var temp = new TempFolder();
        var dict = new Dictionary<string, string?>
        {
            ["Sora:Storage:Profiles:main:Provider"] = "local",
            ["Sora:Storage:Profiles:main:Container"] = "bucket",
            ["Sora:Storage:DefaultProfile"] = "main",
            ["Sora:Storage:Providers:Local:BasePath"] = temp.Path
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        var services = new ServiceCollection();
        services.AddSoraStorage(config);
        services.AddSoraLocalStorageProvider(config);
        var sp = services.BuildServiceProvider();

        var svc = sp.GetRequiredService<IStorageService>();
        var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("ping"));
        var obj = await svc.PutAsync("", "", "a/b.txt", ms, "text/plain");
        obj.Provider.Should().Be("local");
        obj.Container.Should().Be("bucket");
        (await svc.DeleteAsync("", "", "a/b.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task SingleProfileOnly_Fallback_Works_With_One_Profile()
    {
        using var temp = new TempFolder();
        var dict = new Dictionary<string, string?>
        {
            ["Sora:Storage:Profiles:solo:Provider"] = "local",
            ["Sora:Storage:Profiles:solo:Container"] = "only",
            ["Sora:Storage:Providers:Local:BasePath"] = temp.Path
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        var services = new ServiceCollection();
        services.AddSoraStorage(config);
        services.AddSoraLocalStorageProvider(config);
        var sp = services.BuildServiceProvider();

        var svc = sp.GetRequiredService<IStorageService>();
        var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("pong"));
        var obj = await svc.PutAsync("", "", "x/y.txt", ms, "text/plain");
        obj.Provider.Should().Be("local");
        obj.Container.Should().Be("only");
        (await svc.DeleteAsync("", "", "x/y.txt")).Should().BeTrue();
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sora-storage-tests-" + Guid.NewGuid().ToString("N"));
        public TempFolder() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
