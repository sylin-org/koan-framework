using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Json;
using Xunit;

namespace Sora.Data.Core.Tests;

public class SetRoutingTests
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildJson(string dir)
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("Sora:Data:Json:DirectoryPath", dir) })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSora();
        // Provide naming resolver defaults to satisfy registry
        sc.AddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        sc.AddJsonAdapter(o => o.DirectoryPath = dir);
        var sp = sc.BuildServiceProvider();
    try { Sora.Core.SoraEnv.TryInitialize(sp); } catch { }
    (sp.GetService(typeof(Sora.Core.Hosting.Runtime.IAppRuntime)) as Sora.Core.Hosting.Runtime.IAppRuntime)?.Discover();
    (sp.GetService(typeof(Sora.Core.Hosting.Runtime.IAppRuntime)) as Sora.Core.Hosting.Runtime.IAppRuntime)?.Start();
        return sp;
    }

    [Fact]
    public async Task Json_Resolves_Root_And_Backup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sora-set-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var sp = BuildJson(dir);
    Sora.Core.Hosting.App.AppHost.Current = sp;
    // Greenfield boot: ambient provider + runtime start
    try { Sora.Core.SoraEnv.TryInitialize(sp); } catch { }
    (sp.GetService(typeof(Sora.Core.Hosting.Runtime.IAppRuntime)) as Sora.Core.Hosting.Runtime.IAppRuntime)?.Discover();
    (sp.GetService(typeof(Sora.Core.Hosting.Runtime.IAppRuntime)) as Sora.Core.Hosting.Runtime.IAppRuntime)?.Start();

        // root (no suffix)
        var t1 = new Todo { Title = "root-1" };
        await t1.Save();
        (await Data<Todo, string>.All()).Count.Should().Be(1);

        // backup set
        await Data<Todo, string>.UpsertAsync(new Todo { Title = "backup-1" }, set: "backup");
        var inBackup = await Data<Todo, string>.All(set: "backup");
        inBackup.Should().ContainSingle(x => x.Title == "backup-1");

        // ensure isolation between sets
        var inRoot = await Data<Todo, string>.All();
        inRoot.Should().OnlyContain(x => x.Title == "root-1");

        // delete by predicate in backup
        var removed = await Data<Todo, string>.Delete(x => x.Title.StartsWith("backup"), set: "backup");
        removed.Should().Be(1);
        (await Data<Todo, string>.All("backup")).Should().BeEmpty();
    }
}
