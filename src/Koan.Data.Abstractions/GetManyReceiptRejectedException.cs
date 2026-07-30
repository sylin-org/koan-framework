namespace Koan.Data.Abstractions;

/// <summary>Rejects a keyed-read result containing an identity the caller did not request.</summary>
public sealed class GetManyReceiptRejectedException : InvalidOperationException
{
    public GetManyReceiptRejectedException(string entityType)
        : base(
            $"The get-many result for '{entityType}' contained an unrequested identity. " +
            "Correct the adapter so it returns only requested records; missing records must remain null slots.")
    {
        EntityType = entityType;
    }

    public string EntityType { get; }
}
