using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Core;
using Koan.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// Every public composition entry path compiles the typed contributions of the modules it activates, so an active
/// Koan.Tenancy is never left on <see cref="SegmentationPlan.Empty"/>. NK-034 (<c>AddKoanDataCore()</c> activated modules
/// without compiling them) and NK-035 (<c>StartKoan()</c> committed the compiled plans into the caller's abandoned
/// collection instead of the host it built).
/// </summary>
public sealed class CompositionEntryPathSpec : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Koan-Tenancy", Guid.CreateVersion7().ToString("n"));
    private readonly IServiceProvider? _initialHost = AppHost.Current;

    public CompositionEntryPathSpec()
    {
        Directory.CreateDirectory(_root);
        TestHooks.ResetDataConfigs();
    }

    public void Dispose()
    {
        AppHost.Current = _initialHost;
        TestHooks.ResetDataConfigs();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task AddKoanDataCore_compiles_the_tenant_dimension_and_refuses_an_unscoped_write()
    {
        var services = Services();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = "Test",
            ApplicationName = nameof(CompositionEntryPathSpec),
            ContentRootPath = _root,
        });

        services.AddKoanDataCore();
        using var provider = services.BuildServiceProvider();

        TenantDimension(provider).Should().Equal("tenant");
        AppHost.Current = provider;
        var unscoped = async () => await Ledger.Upsert(new Ledger { Title = "x" });
        await unscoped.Should().ThrowAsync<SegmentationRequiredException>()
            .WithMessage("*requires isolation context 'tenant'*");
    }

    [Fact]
    public void AddKoanCore_then_AddKoanDataCore_composes_one_host()
    {
        var services = Services();

        services.AddKoanCore();
        services.AddKoanDataCore();
        services.AddKoan();
        using var provider = services.BuildServiceProvider();

        TenantDimension(provider).Should().Equal("tenant");
    }

    [Fact]
    public async Task StartKoan_over_AddKoanDataCore_runs_with_the_compiled_tenant_dimension()
    {
        var services = Services();
        services.AddKoanDataCore();

        using var host = services.StartKoan();

        TenantDimension(host.Services).Should().Equal("tenant");
        var unscoped = async () => await Ledger.Upsert(new Ledger { Title = "x" });
        await unscoped.Should().ThrowAsync<SegmentationRequiredException>();
    }

    [Fact]
    public void StartKoan_over_a_partially_composed_collection_commits_into_the_host_it_builds()
    {
        var services = Services();
        services.AddKoanCore();

        using var host = services.StartKoan();

        TenantDimension(host.Services).Should().Equal("tenant");
        services.Where(static descriptor => descriptor.ServiceType == typeof(SegmentationPlan))
            .Select(static descriptor => descriptor.ImplementationInstance as SegmentationPlan)
            .Should().OnlyContain(
                plan => plan == null || plan.IsEmpty,
                "the compiled plan belongs to the host StartKoan built, not to the caller's abandoned collection");
    }

    [Fact]
    public void StartKoan_over_a_composed_collection_keeps_its_composition()
    {
        var services = Services();
        services.AddKoan();

        using var host = services.StartKoan();

        TenantDimension(host.Services).Should().Equal("tenant");
    }

    private static string[] TenantDimension(IServiceProvider provider)
        => provider.GetRequiredService<SegmentationPlan>().Dimensions.Select(static d => d.Id).ToArray();

    private ServiceCollection Services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:AllowMagicInProduction"] = "true",
                ["Koan:Tenancy:Posture"] = "Closed",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(_root, "entry.db")}",
                ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
                ["Koan:Storage:Providers:Local:BasePath"] = Path.Combine(_root, "storage"),
                ["Koan:Storage:DefaultProfile"] = "test",
                ["Koan:Storage:Profiles:test:Provider"] = "local",
                ["Koan:Storage:Profiles:test:Container"] = "blobs",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        return services;
    }

    private sealed class Ledger : Koan.Data.Core.Model.Entity<Ledger, string>
    {
        public string Title { get; set; } = "";
    }
}
