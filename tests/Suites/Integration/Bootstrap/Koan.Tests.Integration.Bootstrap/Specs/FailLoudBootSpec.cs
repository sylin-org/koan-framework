using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// ARCH-0079 integration spec for the Track F fail-loud boot policy (fail-fast.json).
/// Proves through real <c>AddKoan()</c> reflective discovery that a broken module registration:
/// (a) crashes the host with a <see cref="KoanBootException"/> that NAMES the module, and
/// (b) remains fail-closed under <c>KOAN_BOOT_LENIENT=1</c> while recording the failure into the
/// registry summary (the MODULES-FAILED channel the boot report renders).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a gated fake:</b> <see cref="ThrowingBootModule"/> is a real <see cref="KoanModule"/>
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
    public void Throwing_module_fails_boot_with_KoanBootException_naming_the_module()
    {
        Environment.SetEnvironmentVariable(LenientEnvVar, null);
        ThrowingBootModule.ResetForTest();
        using var arm = ThrowingBootModule.Arm();

        // AddKoan() drives real reflective discovery; the discovered throwing initializer must abort
        // boot with a KoanBootException — NOT be silently swallowed.
        var act = () => KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .Build();

        var ex = act.Should().Throw<KoanBootException>()
            .Which;

        // The exception is the boot-time diagnostic channel: it MUST name the failing module.
        ex.Module.Should().Be<ThrowingBootModule>();
        ex.Message.Should().Contain(typeof(ThrowingBootModule).FullName);
        ex.Phase.Should().Be("register");
        ex.InnerException.Should().BeOfType<ThrowingBootModule.BootBoomException>();
        ex.Fact.Code.Should().Be(Constants.Diagnostics.Codes.ModuleRejected);
        ex.Fact.Subject.Should().Be(typeof(ThrowingBootModule).FullName);
        ex.Fact.Summary.Should().NotContain("intentionally exploded",
            "the machine fact must not copy arbitrary exception text");

        // (g) The other diagnostic fields carry the provenance an operator needs: which assembly declared
        // the broken module, its version, and — crucially — how to recover (the lenient-boot escape hatch).
        var declaringAssembly = typeof(ThrowingBootModule).Assembly.GetName();
        ex.Assembly.Should().Be(declaringAssembly.Name);
        ex.Version.Should().NotBeNullOrWhiteSpace();
        ex.Version.Should().NotBe("unknown");
        ex.Message.Should().Contain(declaringAssembly.Name);
        ex.Message.Should().Contain("KOAN_BOOT_LENIENT=1");

        // The fake actually ran (guards against a vacuous pass where discovery never reached it).
        ThrowingBootModule.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Clean_boot_records_no_module_failures()
    {
        // (l) Pins the no-false-positive direction: a normal boot (the throwing fake left DISARMED) must
        // leave the MODULES-FAILED channel empty — the fail-loud machinery must not invent failures.
        Environment.SetEnvironmentVariable(LenientEnvVar, null);
        ThrowingBootModule.ResetForTest();
        // Intentionally NOT armed: ThrowingBootModule stays dormant for this boot.

        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        host.Services.Should().NotBeNull();
        ThrowingBootModule.WasInvoked.Should().BeFalse();

        var summary = AppBootstrapper.RegistrySummary;
        summary.Should().NotBeNull();
        summary!.Value.ModuleFailures.Count.Should().Be(0);
    }

    [Fact]
    public void Lenient_manifest_policy_cannot_continue_a_rejected_constitution()
    {
        Environment.SetEnvironmentVariable(LenientEnvVar, "1");
        ThrowingBootModule.ResetForTest();
        using var arm = ThrowingBootModule.Arm();

        try
        {
            var act = () => KoanIntegrationHost.Configure()
                .ConfigureServices(services => services.AddKoan())
                .Build();

            act.Should().Throw<KoanBootException>().Which.Phase.Should().Be("register");
            ThrowingBootModule.WasInvoked.Should().BeTrue();

            // ...and the failure must be VISIBLE in the registry summary (MODULES-FAILED channel),
            // not vanish. This is the recorded boot-report evidence fail-fast.json requires.
            var summary = AppBootstrapper.RegistrySummary;
            summary.Should().NotBeNull();
            summary!.Value.ModuleFailures.Should()
                .ContainSingle(f => f.Module == typeof(ThrowingBootModule).FullName
                                    && f.Phase == "register");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LenientEnvVar, null);
        }
    }
}

/// <summary>
/// A discoverable <see cref="KoanModule"/> that throws when armed. Default = no-op so it does
/// not poison every other spec's <c>AddKoan()</c> (it is AppDomain-globally registered once scanned).
/// </summary>
public sealed class ThrowingBootModule : KoanModule
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

    public override void Register(IServiceCollection services)
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
