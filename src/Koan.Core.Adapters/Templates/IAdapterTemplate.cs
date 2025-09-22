using Koan.Orchestration.Models;

namespace Koan.Core.Adapters.Templates;

/// <summary>
/// Interface for adapter code generation templates.
/// Provides scaffolding for different types of Koan adapters.
/// </summary>
public interface IAdapterTemplate
{
    /// <summary>
    /// The template name and category
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this template generates
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Service type this template is designed for
    /// </summary>
    ServiceType ServiceType { get; }

    /// <summary>
    /// Generate adapter code from template parameters
    /// </summary>
    string GenerateCode(AdapterTemplateParameters parameters);

    /// <summary>
    /// Get required and optional parameters for this template
    /// </summary>
    AdapterTemplateDefinition GetTemplateDefinition();
}