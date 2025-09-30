using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Storage.Extensions;
using Koan.Storage.Connector.Local;
using Koan.Storage.Model;
using Koan.Storage.Options;
using System.Text;
using Xunit;

public class ModelApiTests
{
    private static IServiceProvider BuildServices(string basePath)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Koan:Storage:Profiles:hot:Provider"] = "local",
            ["Koan:Storage:Profiles:hot:Container"] = "hot",
            ["Koan:Storage:Profiles:cold:Provider"] = "local",
            ["Koan:Storage:Profiles:cold:Container"] = "cold",
            ["Koan:Storage:DefaultProfile"] = "hot",
            ["Koan:Storage:Providers:Local:BasePath"] = basePath
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        var services = new ServiceCollection();
        services.AddKoanStorage(config);
        services.AddKoanLocalStorageProvider(config);
        return services.BuildServiceProvider();
    }

    [Koan.Storage.Infrastructure.StorageBinding("hot")]
    private sealed class FileA : StorageEntity<FileA> { }

    [Koan.Storage.Infrastructure.StorageBinding("cold")]
    private sealed class FileB : StorageEntity<FileB> { }

    [Fact]
    public async Task Model_Create_Read_Copy_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        // Ambient DI for model-centric API
        Koan.Core.Hosting.App.AppHost.Current = sp;
        var storage = sp.GetRequiredService<IStorageService>();

        // Create via model
        var rec = await FileA.CreateTextFile("name.txt", "hello");
        rec.Provider.Should().Be("local");
        rec.Container.Should().Be("hot");

        // Read via lightweight proxy
        var text = await FileA.Get(rec.Key).ReadAllText();
        text.Should().Be("hello");

        // Copy to FileB (cold profile)
        var b = await FileA.Get(rec.Key).CopyTo<FileB>();
        b.Provider.Should().Be("local");
        b.Container.Should().Be("cold");
        (await storage.ExistsAsync("cold", "", rec.Key)).Should().BeTrue();

        // Cleanup
        await storage.EnsureDeleted("hot", "", rec.Key);
        await storage.EnsureDeleted("cold", "", rec.Key);
    }

    [Fact]
    public async Task Model_Move_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        Koan.Core.Hosting.App.AppHost.Current = sp;
        var storage = sp.GetRequiredService<IStorageService>();

        var rec = await FileA.CreateTextFile("move.txt", "data");
        (await storage.ExistsAsync("hot", "", rec.Key)).Should().BeTrue();

        var moved = await FileA.Get(rec.Key).MoveTo<FileB>();
        moved.Container.Should().Be("cold");
        (await storage.ExistsAsync("hot", "", rec.Key)).Should().BeFalse();
        (await storage.ExistsAsync("cold", "", rec.Key)).Should().BeTrue();

        await storage.EnsureDeleted("cold", "", rec.Key);
    }

    [Fact]
    public async Task Model_OpenRead_And_Range_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        Koan.Core.Hosting.App.AppHost.Current = sp;

        var content = "abcdefghijklmnopqrstuvwxyz";
        var rec = await FileA.CreateTextFile("alpha.txt", content, "text/plain; charset=utf-8");

        // Instance OpenRead
        await using (var s = await FileA.Get(rec.Key).OpenRead())
        using (var sr = new StreamReader(s))
        {
            var all = await sr.ReadToEndAsync();
            all.Should().Be(content);
        }

        // Instance OpenReadRange (5..9 inclusive => 5 chars)
        var (rangeStream, len) = await FileA.Get(rec.Key).OpenReadRange(5, 9);
        len.Should().Be(5);
        await using (rangeStream)
        using (var sr2 = new StreamReader(rangeStream))
        {
            var part = await sr2.ReadToEndAsync();
            part.Should().Be(content.Substring(5, 5));
        }
    }

    [Fact]
    public async Task Model_KeyFirst_OpenRead_And_Head_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        Koan.Core.Hosting.App.AppHost.Current = sp;

        var bytes = Encoding.UTF8.GetBytes("hello-world");
        var rec = await FileA.Create("blob.bin", bytes: (ReadOnlyMemory<byte>)bytes);

        // Static OpenRead
        await using (var s = await FileA.OpenRead(rec.Key))
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms);
            ms.ToArray().Should().BeEquivalentTo(bytes);
        }

        // Static Head
        var stat = await FileA.Head(rec.Key);
        stat.Should().NotBeNull();
        stat!.Length.Should().Be(bytes.Length);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Koan-storage-model-tests-" + Guid.NewGuid().ToString("N"));
        public TempFolder() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}

