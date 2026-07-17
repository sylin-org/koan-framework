namespace OrderIntake.Domain;

/// <summary>The configured business destination for one order-intake trial.</summary>
public enum WorkloadTarget
{
    Local,
    Documents,
    Relational,
    KeyValue
}
