using System;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Connector.Json;
using Xunit;

namespace Koan.Data.Core.Tests;

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
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("Koan:Data:Json:DirectoryPath", dir) })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddKoan();
        // Provide naming resolver defaults to satisfy registry
        sc.AddSingleton<Koan.Data.Abstractions.Naming.IStorageNameResolver, Koan.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        sc.AddJsonAdapter(o => o.DirectoryPath = dir);
        var sp = sc.BuildServiceProvider();
        try { Koan.Core.KoanEnv.TryInitialize(sp); } catch { }
    (sp.GetService(typeof(Koan.Core.Hosting.Runtime.IAppRuntime)) as Koan.Core.Hosting.Runtime.IAppRuntime)?.Discover();
        (sp.GetService(typeof(Koan.Core.Hosting.Runtime.IAppRuntime)) as Koan.Core.Hosting.Runtime.IAppRuntime)?.Start();
        return sp;
    }

    [Fact]
    public async Task Json_Resolves_Root_And_Backup()
    {
        TestHooks.ResetDataConfigs();

        var dir = Path.Combine(Path.GetTempPath(), "Koan-set-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var sp = BuildJson(dir);
        Koan.Core.Hosting.App.AppHost.Current = sp;
        // Greenfield boot: ambient provider + runtime start
        try { Koan.Core.KoanEnv.TryInitialize(sp); } catch { }
    (sp.GetService(typeof(Koan.Core.Hosting.Runtime.IAppRuntime)) as Koan.Core.Hosting.Runtime.IAppRuntime)?.Discover();
        (sp.GetService(typeof(Koan.Core.Hosting.Runtime.IAppRuntime)) as Koan.Core.Hosting.Runtime.IAppRuntime)?.Start();

        await Data<Todo, string>.DeleteAllAsync();
        await Data<Todo, string>.DeleteAllAsync("backup");

        var rootTitle = "root-" + Guid.NewGuid().ToString("n");
        var backupTitle = "backup-" + Guid.NewGuid().ToString("n");

        // root (no suffix)
        var t1 = new Todo { Title = rootTitle };
        await t1.Save();
        (await Data<Todo, string>.All()).Should().ContainSingle(x => x.Title == rootTitle);

        // backup set
        await Data<Todo, string>.UpsertAsync(new Todo { Title = backupTitle }, partition: "backup");
        var inBackup = await Data<Todo, string>.All(partition: "backup");
        inBackup.Should().ContainSingle(x => x.Title == backupTitle);

        // ensure isolation between sets
        var inRoot = await Data<Todo, string>.All();
        inRoot.Should().ContainSingle(x => x.Title == rootTitle);

        // delete by predicate in backup
        var removed = await Data<Todo, string>.Delete(x => x.Title == backupTitle, partition: "backup");
        removed.Should().Be(1);
        (await Data<Todo, string>.All("backup")).Should().BeEmpty();
    }
}

