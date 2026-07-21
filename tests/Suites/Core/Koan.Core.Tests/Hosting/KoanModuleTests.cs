using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Hosting.Modules;
using Koan.Core.Ordering;
using Koan.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Core.Tests.Hosting;

/// <summary>Conformance specs for the boot-time module primitive (ARCH-0086).</summary>
public class KoanModuleTests
{
    private sealed class DescriptorBackedModule(string owner, List<string> starts) : KoanModule
    {
        public override Task Start(IServiceProvider services, CancellationToken ct)
        {
            starts.Add(owner);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSemanticModule : KoanModule
    {
        public override void Register(IServiceCollection services) =>
            throw new InvalidOperationException("planted semantic registration failure");
    }

    [Fact]
    public async Task Host_starts_the_retained_semantic_instance()
    {
        const string componentId = "Sylin.Koan.DescriptorBacked";
        var starts = new List<string>();
        using var reader = new StringReader($"schema|1{Environment.NewLine}reference|package|{componentId}|{componentId}");
        var manifest = KoanApplicationReferenceManifest.Parse(reader);
        var descriptor = new SemanticComponentDescriptor(
            componentId,
            typeof(DescriptorBackedModule),
            () => new DescriptorBackedModule("semantic", starts));
        var runtime = SemanticModuleRuntime.Create(
            SemanticActivationCompiler.Compile(manifest, [descriptor]));
        var services = new ServiceCollection().BuildServiceProvider();
        var host = new KoanModuleHost(services, runtime);

        await host.StartAsync(CancellationToken.None);

        starts.Should().Equal("semantic");
    }

    [Fact]
    public void Registration_failure_rejects_the_semantic_runtime()
    {
        const string componentId = "Sylin.Koan.FailClosed";
        using var reader = new StringReader(
            $"schema|1{Environment.NewLine}reference|package|{componentId}|{componentId}");
        var descriptor = new SemanticComponentDescriptor(
            componentId,
            typeof(FailingSemanticModule),
            static () => new FailingSemanticModule());
        var runtime = SemanticModuleRuntime.Create(
            SemanticActivationCompiler.Compile(
                KoanApplicationReferenceManifest.Parse(reader),
                [descriptor]));
        var failure = Assert.Throws<SemanticModuleRuntime.SemanticRuntimeException>(() =>
            runtime.TryRegister(typeof(FailingSemanticModule), new ServiceCollection()));

        failure.Problem.Reason.Should().Be("module-registration-failed");
    }
}
