using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Storage.Extensions;
using Koan.Storage.Connector.Local;
using Xunit;

public class LocalProviderEdgeTests
{
    private static IServiceProvider BuildServices(string basePath)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Koan:Storage:Profiles:default:Provider"] = "local",
            ["Koan:Storage:Profiles:default:Container"] = "test",
            ["Koan:Storage:Providers:Local:BasePath"] = basePath
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        var services = new ServiceCollection();
        services.AddKoanStorage(config);
        services.AddKoanLocalStorageProvider(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task NonSeek_Upload_Works()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        // Non-seek stream
        var pipe = new System.IO.Pipelines.Pipe();
        var data = System.Text.Encoding.UTF8.GetBytes("abc123");
        await pipe.Writer.WriteAsync(data);
        await pipe.Writer.FlushAsync();
        pipe.Writer.Complete();
        var readerStream = pipe.Reader.AsStream(leaveOpen: false);

        var obj = await svc.PutAsync("default", "", "a/b/nonseek.txt", readerStream, "text/plain");
        obj.Size.Should().Be(0); // not computed for non-seek input in MVP

        await using var read = await svc.ReadAsync("default", "", "a/b/nonseek.txt");
        using var sr = new StreamReader(read);
        (await sr.ReadToEndAsync()).Should().Be("abc123");
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("./evil.txt")]
    [InlineData("a/..//evil.txt")]
    public async Task Traversal_Is_Rejected_On_Write(string key)
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("x"));
        await FluentActions.Invoking(() => svc.PutAsync("default", "", key, content, "text/plain"))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Range_Validation()
    {
        using var temp = new TempFolder();
        var sp = BuildServices(temp.Path);
        var svc = sp.GetRequiredService<IStorageService>();

        await svc.PutAsync("default", "", "r.txt", new MemoryStream(System.Text.Encoding.UTF8.GetBytes("0123456789")), "text/plain");
        // start beyond end
        await FluentActions.Invoking(() => svc.ReadRangeAsync("default", "", "r.txt", 100, null))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Koan-storage-tests-" + Guid.NewGuid().ToString("N"));
        public TempFolder() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}

