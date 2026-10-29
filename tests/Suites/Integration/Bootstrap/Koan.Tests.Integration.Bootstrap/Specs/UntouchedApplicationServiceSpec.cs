using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// ARCH-0139: a host composed before any of an application assembly's code has run still registers and starts that
/// assembly's background services. The process is made fresh for Koan by an isolated load context: Koan.Core and the
/// application load there with empty registries, the application's generated module initializer has not run, and the
/// host is composed through that Koan.Core's <c>AddKoan()</c>. The application is built into its own folder under the
/// test output, so no default-context composition in this process can scan it, whatever order the specs run in.
/// </summary>
public sealed class UntouchedApplicationServiceSpec
{
    private const string ApplicationAssembly = "Koan.Tests.Integration.Bootstrap.UntouchedApplication";
    private const string WorkerTypeName = ApplicationAssembly + ".UntouchedWorker";
    private const string KoanPrefix = "Koan.";
    private const string ApplicationFolder = "untouched-application";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task A_host_composed_before_the_application_ran_registers_and_starts_its_background_service()
    {
        using var timeout = new CancellationTokenSource(Bound);
        AssemblyLoadContext.Default.Assemblies
            .Any(assembly => assembly.GetName().Name == ApplicationAssembly)
            .Should().BeFalse("no code in the default context may have reached the application");
        var fresh = new FreshKoan(AppContext.BaseDirectory);
        try
        {
            var application = fresh.LoadFromAssemblyPath(
                Path.Combine(AppContext.BaseDirectory, ApplicationFolder, ApplicationAssembly + ".dll"));
            var addKoan = fresh.LoadFromAssemblyName(new AssemblyName("Koan.Core"))
                .GetType("Koan.Core.ServiceCollectionExtensions", throwOnError: true)!
                .GetMethod("AddKoan", [typeof(IServiceCollection)])!;

            var builder = Host.CreateApplicationBuilder();
            addKoan.Invoke(null, [builder.Services]);

            builder.Services
                .Any(descriptor => descriptor.ServiceType.FullName == WorkerTypeName && descriptor.ServiceType.Assembly == application)
                .Should().BeTrue("composition registers the background services of every assembly it discovered, whatever code ran before it");

            using var host = builder.Build();
            await host.StartAsync(timeout.Token);
            try
            {
                var started = (Task)application.GetType(WorkerTypeName, throwOnError: true)!
                    .GetProperty("Started", BindingFlags.Public | BindingFlags.Static)!
                    .GetValue(null)!;
                await started.WaitAsync(timeout.Token);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            fresh.Unload();
        }
    }

    /// <summary>Koan's assemblies, loaded fresh from the test output; everything else is shared with the test.</summary>
    private sealed class FreshKoan(string directory) : AssemblyLoadContext(nameof(FreshKoan), isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not { } name || !name.StartsWith(KoanPrefix, StringComparison.Ordinal)) return null;
            var path = Path.Combine(directory, name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
