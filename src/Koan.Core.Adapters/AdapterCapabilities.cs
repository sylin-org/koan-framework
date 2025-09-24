using Koan.Data.Abstractions;

namespace Koan.Core.Adapters;

/// <summary>
/// Comprehensive capability declaration for a Koan adapter.
/// Enables runtime capability querying and feature detection.
/// </summary>
public class AdapterCapabilities
{
    public HealthCapabilities Health { get; private set; }
    public ConfigurationCapabilities Configuration { get; private set; }
    public SecurityCapabilities Security { get; private set; }
    public MessagingCapabilities Messaging { get; private set; }
    public OrchestrationCapabilities Orchestration { get; private set; }
    public ExtendedQueryCapabilities Data { get; private set; }
    public QueryCapabilities QueryCapabilities { get; private set; }
    public List<string> CustomCapabilities { get; private set; } = new();

    public static AdapterCapabilities Create() => new();

    public AdapterCapabilities WithHealth(HealthCapabilities capabilities)
    {
        Health |= capabilities;
        return this;
    }

    public AdapterCapabilities WithConfiguration(ConfigurationCapabilities capabilities)
    {
        Configuration |= capabilities;
        return this;
    }

    public AdapterCapabilities WithSecurity(SecurityCapabilities capabilities)
    {
        Security |= capabilities;
        return this;
    }

    public AdapterCapabilities WithMessaging(MessagingCapabilities capabilities)
    {
        Messaging |= capabilities;
        return this;
    }

    public AdapterCapabilities WithOrchestration(OrchestrationCapabilities capabilities)
    {
        Orchestration |= capabilities;
        return this;
    }

    public AdapterCapabilities WithData(ExtendedQueryCapabilities capabilities)
    {
        Data |= capabilities;
        return this;
    }

    public AdapterCapabilities WithQueryCapabilities(QueryCapabilities capabilities)
    {
        QueryCapabilities |= capabilities;
        return this;
    }

    public AdapterCapabilities WithCustom(params string[] capabilities)
    {
        foreach (var capability in capabilities)
        {
            if (!string.IsNullOrWhiteSpace(capability) && !CustomCapabilities.Contains(capability))
            {
                CustomCapabilities.Add(capability);
            }
        }
        return this;
    }

    /// <summary>
    /// Check if this adapter supports specific data capabilities
    /// </summary>
    public bool SupportsData(ExtendedQueryCapabilities capabilities)
        => (Data & capabilities) == capabilities;

    /// <summary>
    /// Check if this adapter supports specific health capabilities
    /// </summary>
    public bool SupportsHealth(HealthCapabilities capabilities)
        => (Health & capabilities) == capabilities;

    /// <summary>
    /// Check if this adapter supports specific orchestration capabilities
    /// </summary>
    public bool SupportsOrchestration(OrchestrationCapabilities capabilities)
        => (Orchestration & capabilities) == capabilities;

    /// <summary>
    /// Check if this adapter supports specific configuration capabilities
    /// </summary>
    public bool SupportsConfiguration(ConfigurationCapabilities capabilities)
        => (Configuration & capabilities) == capabilities;

    /// <summary>
    /// Check if this adapter supports specific messaging capabilities
    /// </summary>
    public bool SupportsMessaging(MessagingCapabilities capabilities)
        => (Messaging & capabilities) == capabilities;

    /// <summary>
    /// Check if this adapter supports specific security capabilities
    /// </summary>
    public bool SupportsSecurity(SecurityCapabilities capabilities)
        => (Security & capabilities) == capabilities;

    /// <summary>
    /// Check if this adapter supports a custom capability
    /// </summary>
    public bool SupportsCustom(string capability)
        => CustomCapabilities.Contains(capability);

    /// <summary>
    /// Get a comprehensive capability summary for debugging/reporting
    /// </summary>
    public Dictionary<string, object> GetCapabilitySummary()
    {
        var summary = new Dictionary<string, object>();

        if (Health != HealthCapabilities.None)
            summary["Health"] = Health.ToString();

        if (Configuration != ConfigurationCapabilities.None)
            summary["Configuration"] = Configuration.ToString();

        if (Security != SecurityCapabilities.None)
            summary["Security"] = Security.ToString();

        if (Messaging != MessagingCapabilities.None)
            summary["Messaging"] = Messaging.ToString();

        if (Orchestration != OrchestrationCapabilities.None)
            summary["Orchestration"] = Orchestration.ToString();

        if (Data != ExtendedQueryCapabilities.None)
            summary["Data"] = Data.ToString();

        if (QueryCapabilities != QueryCapabilities.None)
            summary["QueryCapabilities"] = QueryCapabilities.ToString();

        if (CustomCapabilities.Any())
            summary["Custom"] = CustomCapabilities.ToArray();

        return summary;
    }
}