using Koan.Core.Adapters;

namespace Koan.Data.Adapters.Configuration;

/// <summary>
/// Common interface for adapter configuration options.
/// Defines standard settings shared across all data adapters.
/// </summary>
/// <remarks>
/// Page-size capping is intentionally NOT part of this interface. Per-connector
/// <c>MaxPageSize</c> caps were removed in favour of treating page-size enforcement as a
/// concern of the consuming layer (e.g. <c>Koan.Web</c>'s <c>PaginationSafetyBounds</c>),
/// not the storage adapter. <see cref="DefaultPageSize"/> remains as a sane fallback for
/// callers that don't pass an explicit page size; it is not a cap.
/// </remarks>
public interface IAdapterOptions
{
    /// <summary>
    /// Adapter readiness configuration for initialization and health monitoring
    /// </summary>
    IAdapterReadinessConfiguration Readiness { get; }

    /// <summary>
    /// Default page size used when callers don't specify one. NOT a cap — callers may
    /// request larger sizes and adapters honour them. Output-layer enforcement (e.g.
    /// HTTP API limits) lives elsewhere.
    /// </summary>
    int DefaultPageSize { get; set; }
}
