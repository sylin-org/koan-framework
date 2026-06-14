using System;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>
/// The single fail-loud signal for boot-time module failures (Track F · fail-fast.json).
/// Thrown by <see cref="AppBootstrapper.InitializeModules"/> when an <see cref="IKoanInitializer"/>
/// construction / <c>Initialize()</c> call throws, or when the manifest-invoker itself fails —
/// unless degraded boot is requested via the <c>KOAN_BOOT_LENIENT=1</c> environment variable.
/// </summary>
/// <remarks>
/// <para>
/// No <c>ILogger</c> exists at <c>InitializeModules</c> time (it runs on <c>IServiceCollection</c>
/// before the provider/configuration are built), so the exception itself is the diagnostic channel
/// and MUST name the failing module. This mirrors the framework's existing boot-time fail-fast
/// canon in <c>KoanBackgroundServiceOrchestrator.FailFastOnStartupFailure</c> and .NET 6's
/// <c>BackgroundService</c> StopHost migration. Per fail-fast.json this is the ONLY new exception
/// type — no <c>KoanException</c> hierarchy / <c>KoanDataException</c> provider-wrapping is added.
/// </para>
/// </remarks>
public sealed class KoanBootException : Exception
{
    /// <summary>Creates the exception naming the failing <paramref name="module"/> and boot <paramref name="phase"/>.</summary>
    public KoanBootException(Type module, string assembly, string version, string phase, Exception inner)
        : base($"Koan boot failed: module '{module.FullName ?? module.Name}' (assembly '{assembly}' {version}) threw during {phase}. " +
               $"Set environment variable KOAN_BOOT_LENIENT=1 to boot in degraded mode and surface this in the MODULES-FAILED boot report block instead. " +
               $"Inner: {inner.Message}", inner)
    {
        Module = module;
        Assembly = assembly;
        Version = version;
        Phase = phase;
    }

    /// <summary>The module type whose construction / initialization failed.</summary>
    public Type Module { get; }

    /// <summary>The simple name of the assembly that declares the failing module.</summary>
    public string Assembly { get; }

    /// <summary>The version of the assembly that declares the failing module.</summary>
    public string Version { get; }

    /// <summary>The boot phase in which the failure occurred (e.g. <c>initializer</c>, <c>manifest-invoker</c>).</summary>
    public string Phase { get; }
}
