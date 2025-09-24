using Koan.Orchestration.Models;

namespace Koan.Core.Adapters.Templates;

/// <summary>
/// Manager for adapter code generation templates.
/// Provides discovery, validation, and code generation capabilities.
/// </summary>
public class AdapterTemplateManager
{
    private readonly Dictionary<string, IAdapterTemplate> _templates = new();

    public AdapterTemplateManager()
    {
        // Register built-in templates
        RegisterTemplate("database", new DatabaseAdapterTemplate());
        RegisterTemplate("ai-vector", new AiVectorAdapterTemplate());
        RegisterTemplate("messaging", new MessagingAdapterTemplate());
    }

    /// <summary>
    /// Get all available templates
    /// </summary>
    public IReadOnlyDictionary<string, IAdapterTemplate> GetAvailableTemplates()
        => _templates.AsReadOnly();

    /// <summary>
    /// Get a specific template by name
    /// </summary>
    public IAdapterTemplate? GetTemplate(string name)
        => _templates.TryGetValue(name.ToLowerInvariant(), out var template) ? template : null;

    /// <summary>
    /// Get templates for a specific service type
    /// </summary>
    public IEnumerable<IAdapterTemplate> GetTemplatesForServiceType(ServiceType serviceType)
        => _templates.Values.Where(t => t.ServiceType == serviceType);

    /// <summary>
    /// Validate template parameters against template definition
    /// </summary>
    public ValidationResult ValidateParameters(IAdapterTemplate template, AdapterTemplateParameters parameters)
    {
        var definition = template.GetTemplateDefinition();
        var errors = new List<string>();

        // Check required parameters
        foreach (var required in definition.RequiredParameters)
        {
            var propertyInfo = typeof(AdapterTemplateParameters).GetProperty(required);
            if (propertyInfo == null)
            {
                errors.Add($"Unknown required parameter: {required}");
                continue;
            }

            var value = propertyInfo.GetValue(parameters);
            if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
            {
                errors.Add($"Required parameter '{required}' is missing or empty");
            }
        }

        // Validate with custom validators
        foreach (var (param, validator) in definition.ParameterValidators)
        {
            var propertyInfo = typeof(AdapterTemplateParameters).GetProperty(param);
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(parameters);
                if (value != null && !validator(value))
                {
                    errors.Add($"Parameter '{param}' failed validation");
                }
            }
        }

        return errors.Any() ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <summary>
    /// Generate adapter code from template and parameters
    /// </summary>
    public string GenerateAdapter(string templateName, AdapterTemplateParameters parameters)
    {
        var template = GetTemplate(templateName);
        if (template == null)
            throw new ArgumentException($"Template '{templateName}' not found", nameof(templateName));

        var validation = ValidateParameters(template, parameters);
        if (!validation.IsValid)
            throw new ArgumentException($"Parameter validation failed: {string.Join(", ", validation.Errors)}");

        return template.GenerateCode(parameters);
    }

    /// <summary>
    /// Generate adapter code with automatic parameter completion
    /// </summary>
    public string GenerateAdapter(
        string templateName,
        string className,
        string adapterId,
        string displayName,
        string @namespace,
        ServiceType? serviceType = null)
    {
        var template = GetTemplate(templateName);
        if (template == null)
            throw new ArgumentException($"Template '{templateName}' not found", nameof(templateName));

        var parameters = new AdapterTemplateParameters
        {
            ClassName = className,
            AdapterId = adapterId,
            DisplayName = displayName,
            Namespace = @namespace,
            ServiceType = serviceType ?? template.ServiceType
        };

        return GenerateAdapter(templateName, parameters);
    }

    /// <summary>
    /// Get template usage help including parameters and examples
    /// </summary>
    public string GetTemplateHelp(string templateName)
    {
        var template = GetTemplate(templateName);
        if (template == null)
            return $"Template '{templateName}' not found";

        var definition = template.GetTemplateDefinition();
        var help = new List<string>
        {
            $"Template: {definition.Name}",
            $"Description: {definition.Description}",
            $"Service Type: {definition.ServiceType}",
            "",
            "Required Parameters:"
        };

        foreach (var param in definition.RequiredParameters)
        {
            var description = definition.ParameterDescriptions.TryGetValue(param, out var desc) ? desc : "No description";
            var example = definition.ParameterExamples.TryGetValue(param, out var ex) ? $" (example: {ex})" : "";
            help.Add($"  - {param}: {description}{example}");
        }

        if (definition.OptionalParameters.Any())
        {
            help.Add("");
            help.Add("Optional Parameters:");
            foreach (var (param, defaultValue) in definition.OptionalParameters)
            {
                var description = definition.ParameterDescriptions.TryGetValue(param, out var desc) ? desc : "No description";
                help.Add($"  - {param}: {description} (default: {defaultValue})");
            }
        }

        return string.Join(Environment.NewLine, help);
    }

    /// <summary>
    /// Register a custom template
    /// </summary>
    public void RegisterTemplate(string name, IAdapterTemplate template)
    {
        _templates[name.ToLowerInvariant()] = template;
    }
}