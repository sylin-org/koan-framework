using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core.Hosting.Modules;
using Koan.Core.Provenance;

namespace Koan.Core;

/// <summary>
/// The boot-time module primitive (ARCH-0086): one self-describing unit an assembly author writes to
/// register services, declare ordered startup work, and self-report. It <b>implements</b>
/// <see cref="IKoanAutoRegistrar"/>, so the existing source-generated discovery (<c>KoanRegistry</c>) and
/// topological ordering (<c>RegistrarOrdering</c> via <c>[Before]</c>/<c>[After]</c>) apply unchanged — a
/// <see cref="KoanModule"/> is discovered and ordered exactly like a hand-written <c>KoanAutoRegistrar</c>.
/// <para>
/// Module identity + DI + startup + provenance live here. Per-provider <b>capabilities</b> do NOT — those
/// are a per-type runtime concern a provider declares via <c>IDescribesCapabilities</c> (ARCH-0084), a
/// different granularity than a per-assembly boot module. See ARCH-0086.
/// </para>
/// </summary>
public abstract class KoanModule : IKoanAutoRegistrar
{
    /// <summary>Canonical module id, e.g. <c>"data.postgres"</c>. Surfaces as <see cref="IKoanAutoRegistrar.ModuleName"/>.</summary>
    public abstract string Id { get; }

    /// <summary>Module version; defaults to the declaring assembly's version.</summary>
    public virtual string? Version => GetType().Assembly.GetName().Version?.ToString();

    /// <summary>Register this module's services into the container. Replaces <c>IKoanInitializer.Initialize</c>.</summary>
    public virtual void Register(IServiceCollection services) { }

    /// <summary>
    /// Run one-time startup work, with DI available, ordered against other modules by <c>[Before]</c>/<c>[After]</c>.
    /// Folds the "register a bootstrap <c>IHostedService</c> inside <c>Initialize</c>" idiom into one verb.
    /// (Recurring periodic/pokable work stays on the <c>IKoanBackgroundService</c> family.) Default: no-op.
    /// </summary>
    public virtual Task Start(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Publish this module's provenance self-report. Named <see cref="Report"/> (not <c>Describe</c>) to
    /// disambiguate from the per-provider capability <c>IDescribesCapabilities.Describe</c>. Default: version only.
    /// </summary>
    public virtual void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version);

    // --- IKoanAutoRegistrar bridge: existing discovery + ordering work unchanged ---

    string IKoanAutoRegistrar.ModuleName => Id;
    string? IKoanAutoRegistrar.ModuleVersion => Version;

    void IKoanInitializer.Initialize(IServiceCollection services)
    {
        Register(services);
        // Make this module instance resolvable + ensure the host that runs Start() is registered (idempotent).
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(KoanModule), GetType()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, KoanModuleHost>());
    }

    void IKoanAutoRegistrar.Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => Report(module, cfg, env);
}
