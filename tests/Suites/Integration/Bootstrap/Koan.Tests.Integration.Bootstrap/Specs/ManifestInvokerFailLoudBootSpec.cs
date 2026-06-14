using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// ARCH-0079 integration spec for the SECOND fail-loud source in <see cref="AppBootstrapper"/> — the
/// manifest-invoker (Track F · fail-fast.json, AppBootstrapper.RunManifestLoader). The initializer-loop
/// source is covered by <see cref="FailLoudBootSpec"/>; this spec proves the manifest-invoker:
/// (c) fails boot with a <see cref="KoanBootException"/> whose <c>Phase</c> starts with
///     <c>manifest-invoker</c> when the per-assembly loader throws, and
/// (j) UNWRAPS a <see cref="TargetInvocationException"/> so the recorded / thrown inner cause is the REAL
///     error — never the reflection wrapper's "Exception has been thrown by the target of an invocation"
///     placeholder, and
/// (d) under <c>KOAN_BOOT_LENIENT=1</c> lets the host boot while recording the failure into the registry
///     summary's MODULES-FAILED channel with a <c>manifest-invoker</c> phase.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the test seam:</b> the real per-assembly loader (<c>RegistryManifestLoader.PopulateFromAssembly</c>)
/// is source-generated and swallows every reflection failure internally, so no fixture assembly can make it
/// throw — the escaping-exception branch is unreachable from a test. <see cref="AppBootstrapper"/> exposes the
/// smallest possible internal seam (<c>ManifestLoaderInvocationOverrideForTests</c>) that replaces ONLY the
/// inner loader call; the production unwrap / record / fail-loud wrapper around it stays in force and is the
/// behaviour actually under test here. The override is AppDomain-global, so every test arms it in a
/// <c>try/finally</c> and the suite is serialized via <see cref="FailLoudBootCollection"/>.
/// </para>
/// </remarks>
[Collection(FailLoudBootCollection.Name)]
public sealed class ManifestInvokerFailLoudBootSpec
{
    private const string LenientEnvVar = "KOAN_BOOT_LENIENT";

    private readonly ITestOutputHelper _output;

    public ManifestInvokerFailLoudBootSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Throwing_manifest_invoker_fails_boot_and_unwraps_TargetInvocationException()
    {
        Environment.SetEnvironmentVariable(LenientEnvVar, null);
        var invoked = 0;

        // The seam stands in for the reflected PopulateFromAssembly call. Wrapping the real cause in a
        // TargetInvocationException reproduces exactly what method.Invoke(...) raises in production, so the
        // unwrap branch (var actual = (ex as TargetInvocationException)?.InnerException ?? ex) is exercised.
        var sentinel = new ManifestBoomException();
        AppBootstrapper.ManifestLoaderInvocationOverrideForTests = _ =>
        {
            Interlocked.Increment(ref invoked);
            throw new TargetInvocationException(sentinel);
        };

        try
        {
            var act = () => KoanIntegrationHost.Configure()
                .ConfigureServices(services => services.AddKoan())
                .Build();

            var ex = act.Should().Throw<KoanBootException>().Which;

            // (c) The manifest-invoker is the fail source — phase NAMES it (and the assembly being scanned).
            ex.Phase.Should().StartWith("manifest-invoker");

            // (j) The reflection wrapper MUST be unwrapped: the inner cause and message are the real fault,
            // not "Exception has been thrown by the target of an invocation."
            ex.InnerException.Should().BeSameAs(sentinel);
            ex.InnerException.Should().NotBeOfType<TargetInvocationException>();
            ex.Message.Should().Contain(ManifestBoomException.Sentinel);
            ex.Message.Should().NotContain("target of an invocation");

            // Non-vacuity guard: the seam actually ran (otherwise this would be a false pass where the
            // throwing branch was never reached).
            invoked.Should().BeGreaterThan(0);
        }
        finally
        {
            AppBootstrapper.ManifestLoaderInvocationOverrideForTests = null;
        }
    }

    [Fact]
    public async Task Lenient_boot_records_manifest_invoker_failure_in_registry_summary()
    {
        Environment.SetEnvironmentVariable(LenientEnvVar, "1");
        var invoked = 0;
        var sentinel = new ManifestBoomException();
        AppBootstrapper.ManifestLoaderInvocationOverrideForTests = _ =>
        {
            Interlocked.Increment(ref invoked);
            throw new TargetInvocationException(sentinel);
        };

        try
        {
            // (d) With KOAN_BOOT_LENIENT=1 the manifest-invoker failure must NOT crash the host...
            await using var host = await KoanIntegrationHost.Configure()
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();

            host.Services.Should().NotBeNull();
            invoked.Should().BeGreaterThan(0);

            // ...and the failure must surface in the registry summary (MODULES-FAILED channel) with a
            // manifest-invoker phase and the UNWRAPPED inner error message.
            var summary = AppBootstrapper.RegistrySummary;
            summary.Should().NotBeNull();
            summary!.Value.ModuleFailures.Should()
                .Contain(f => f.Phase.StartsWith("manifest-invoker")
                              && f.Error == ManifestBoomException.Sentinel);
        }
        finally
        {
            AppBootstrapper.ManifestLoaderInvocationOverrideForTests = null;
            Environment.SetEnvironmentVariable(LenientEnvVar, null);
        }
    }

    /// <summary>Distinctive inner cause so the unwrap assertions cannot accidentally pass on the
    /// reflection wrapper's generic message.</summary>
    private sealed class ManifestBoomException : Exception
    {
        public const string Sentinel = "fail-loud manifest-invoker test: per-assembly loader intentionally exploded";

        public ManifestBoomException() : base(Sentinel) { }
    }
}
