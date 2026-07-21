using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core.Provenance;
using Koan.Core.Semantics;
using Koan.Core.Composition;
using System.ComponentModel;

namespace Koan.Core;

/// <summary>
/// The boot-time module primitive (ARCH-0086): one self-describing unit an assembly author writes to
/// register services, declare ordered startup work, and self-report. Every concrete module is generated as
/// a construction-free descriptor under its assembly's standard package identity, filtered
/// by the host constitution, and retained as one instance across registration and startup.
/// <para>
/// Module identity + DI + startup + provenance live here. Per-provider <b>capabilities</b> do NOT — those
/// are a per-type runtime concern a provider declares via <c>IDescribesCapabilities</c> (ARCH-0084), a
/// different granularity than a per-assembly boot module. See ARCH-0086.
/// </para>
/// </summary>
public abstract class KoanModule
{
    private string? _semanticId;

    /// <summary>
    /// Canonical module identity derived from the declaring project's standard package or assembly identity.
    /// Module authors do not declare or override it.
    /// </summary>
    public string Id => _semanticId
        ?? throw new InvalidOperationException(
            $"Koan module '{GetType().FullName}' has no bound identity. Let AddKoan construct modules through the generated host constitution.");

    internal void BindSemanticIdentity(SemanticId id)
    {
        if (_semanticId is null)
        {
            _semanticId = id.Value;
            return;
        }

        if (!string.Equals(_semanticId, id.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Koan module '{GetType().FullName}' cannot be rebound from '{_semanticId}' to '{id}'.");
        }
    }

    /// <summary>Module version; defaults to the declaring assembly's version.</summary>
    public virtual string? Version => GetType().Assembly.GetName().Version?.ToString();

    /// <summary>Register this module's services into the container.</summary>
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

    /// <summary>
    /// Projects this active module's already-resolved runtime decisions into Koan's safe composition
    /// evidence. The retained module instance is the sole lifecycle owner; applications do not call or
    /// register this method.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services) { }

}
