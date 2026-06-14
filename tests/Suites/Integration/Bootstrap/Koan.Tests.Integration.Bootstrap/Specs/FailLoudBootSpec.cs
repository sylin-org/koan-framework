using System;
using System.Linq;
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
/// ARCH-0079 integration spec for the Track F fail-loud boot policy (fail-fast.json).
/// Proves through real <c>AddKoan()</c> reflective discovery that a broken module initializer:
/// (a) crashes the host with a <see cref="KoanBootException"/> that NAMES the module, and
/// (b) under <c>KOAN_BOOT_LENIENT=1</c> lets the host boot while recording the failure into the
/// registry summary (the MODULES-FAILED channel the boot report renders).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a gated fake:</b> <see cref="ThrowingBootInitializer"/> is a real <see cref="IKoanInitializer"/>
/// in this test assembly, so <c>RegistryManifestLoader</c> discovers it on the first <c>AddKoan()</c>
/// in the process and it stays registered in the AppDomain-wide <c>KoanRegistry</c> for every later
/// boot. It must therefore be a no-op unless explicitly armed, or it would break every other spec's
/// <c>AddKoan()</c>. The arm flag flows through <c>AsyncLocal</c> so the throw is scoped to the call
/// chain of the arming test; the suite is also serialized via <see cref="FailLoudBootCollection"/>.
/// </para>
/// </remarks>
[Collection(FailLoudBootCollection.Name)]
public sealed class FailLoudBootSpec
{
    private const string LenientEnvVar = "KOAN_BOOT_LENIENT";
    private readonly ITestOutputHelper _output;

    public FailLoudBootSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Throwing_initializer_fails_boot_with_KoanBootException_naming_the_module()
    {
        Environment.SetEnvironmentVariable(LenientEnvVar, null);
        ThrowingBootInitializer.ResetForTest();
        using var arm = ThrowingBootInitializer.Arm();

        // AddKoan() drives real reflective discovery; the discovered throwing initializer must abort
        // boot with a KoanBootException — NOT be silently swallowed.
        var act = () => KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .Build();

        var ex = act.Should().Throw<KoanBootException>()
            .Which;

        // The exception is the boot-time diagnostic channel: it MUST name the failing module.
        ex.Module.Should().Be<ThrowingBootInitializer>();
        ex.Message.Should().Contain(typeof(ThrowingBootInitializer).FullName);
        ex.Phase.Should().Be("initializer");
        ex.InnerException.Should().BeOfType<ThrowingBootInitializer.BootBoomException>();

        // The fake actually ran (guards against a vacuous pass where discovery never reached it).
        ThrowingBootInitializer.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Lenient_boot_starts_host_and_records_failure_in_registry_summary()
    {
        Environment.SetEnvironmentVariable(LenientEnvVar, "1");
        ThrowingBootInitializer.ResetForTest();
        using var arm = ThrowingBootInitializer.Arm();

        try
        {
            // With KOAN_BOOT_LENIENT=1 the host must BOOT despite the broken module...
            await using var host = await KoanIntegrationHost.Configure()
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();

            host.Services.Should().NotBeNull();
            ThrowingBootInitializer.WasInvoked.Should().BeTrue();

            // ...and the failure must be VISIBLE in the registry summary (MODULES-FAILED channel),
            // not vanish. This is the recorded boot-report evidence fail-fast.json requires.
            var summary = AppBootstrapper.RegistrySummary;
            summary.Should().NotBeNull();
            summary!.Value.ModuleFailures.Should()
                .ContainSingle(f => f.Module == typeof(ThrowingBootInitializer).FullName
                                    && f.Phase == "initializer");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LenientEnvVar, null);
        }
    }
}

/// <summary>
/// A discoverable <see cref="IKoanInitializer"/> that throws when armed. Default = no-op so it does
/// not poison every other spec's <c>AddKoan()</c> (it is AppDomain-globally registered once scanned).
/// </summary>
public sealed class ThrowingBootInitializer : IKoanInitializer
{
    private static readonly AsyncLocal<bool> _armed = new();
    private static int _invoked;

    public static bool WasInvoked => Volatile.Read(ref _invoked) != 0;

    public static IDisposable Arm()
    {
        _armed.Value = true;
        return new Disarm();
    }

    public static void ResetForTest()
    {
        Volatile.Write(ref _invoked, 0);
        _armed.Value = false;
    }

    public void Initialize(IServiceCollection services)
    {
        if (!_armed.Value) return; // dormant for every unrelated boot
        Interlocked.Exchange(ref _invoked, 1);
        throw new BootBoomException();
    }

    public sealed class BootBoomException : Exception
    {
        public BootBoomException() : base("fail-loud boot test: module initializer intentionally exploded") { }
    }

    private sealed class Disarm : IDisposable
    {
        public void Dispose() => _armed.Value = false;
    }
}

[CollectionDefinition(FailLoudBootCollection.Name, DisableParallelization = true)]
public sealed class FailLoudBootCollection
{
    public const string Name = "fail-loud-boot";
}
