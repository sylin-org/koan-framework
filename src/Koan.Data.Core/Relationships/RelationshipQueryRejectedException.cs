namespace Koan.Data.Core.Relationships;

/// <summary>A relationship query was refused before an unbounded or oversized result could escape.</summary>
public sealed class RelationshipQueryRejectedException : InvalidOperationException
{
    public RelationshipQueryRejectedException(
        string parentType,
        string childType,
        string referenceProperty,
        string provider,
        string reasonCode,
        string correction,
        int? limit = null)
        : base($"Relationship {parentType} -> {childType}.{referenceProperty} was rejected by provider '{provider}' " +
               $"({reasonCode}). {correction}")
    {
        ParentType = parentType;
        ChildType = childType;
        ReferenceProperty = referenceProperty;
        Provider = provider;
        ReasonCode = reasonCode;
        Correction = correction;
        Limit = limit;
    }

    public string ParentType { get; }
    public string ChildType { get; }
    public string ReferenceProperty { get; }
    public string Provider { get; }
    public string ReasonCode { get; }
    public string Correction { get; }
    public int? Limit { get; }

    public bool IsLimitExceeded => ReasonCode is Infrastructure.Constants.Diagnostics.Reasons.FallbackLimit
        or Infrastructure.Constants.Diagnostics.Reasons.ResultLimit;
}
