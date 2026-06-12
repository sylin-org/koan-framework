using Microsoft.AspNetCore.Builder;

namespace Koan.Web.Hosting;

/// <summary>
/// The supported, ordering-safe seam for a module to contribute middleware at a named
/// <see cref="KoanWebPipelineStage"/> of Koan's canonical web pipeline. <see cref="KoanWebStartupFilter"/> owns
/// the order and invokes contributors at each stage boundary, so a contributor never depends on
/// <c>IStartupFilter</c> registration order (the failure mode that killed the SEC-0001 dev identity). See WEB-0069.
/// </summary>
public interface IKoanWebPipelineContributor
{
    /// <summary>Where in the pipeline this contributor's middleware is inserted.</summary>
    KoanWebPipelineStage Stage { get; }

    /// <summary>Tie-break within a stage; lower runs first.</summary>
    int Order => 0;

    void Configure(IApplicationBuilder app);
}
