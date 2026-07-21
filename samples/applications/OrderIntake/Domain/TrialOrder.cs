using Koan.Data.Core.Model;

namespace OrderIntake.Domain;

/// <summary>One order accepted and verified by a bounded workload trial.</summary>
public sealed class TrialOrder : Entity<TrialOrder>
{
    public string TrialId { get; set; } = "";
    public int Sequence { get; set; }
    public string CustomerReference { get; set; } = "";
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public static TrialOrder For(string trialId, int sequence) => new()
    {
        Id = $"{trialId}-order-{sequence:D4}",
        TrialId = trialId,
        Sequence = sequence,
        CustomerReference = $"customer-{sequence % 17:D2}",
        Sku = $"sku-{sequence % 29:D2}",
        Quantity = sequence % 5 + 1,
        UnitPrice = 12.50m + sequence % 11
    };

    public bool Matches(TrialOrder expected) =>
        Id == expected.Id
        && TrialId == expected.TrialId
        && Sequence == expected.Sequence
        && CustomerReference == expected.CustomerReference
        && Sku == expected.Sku
        && Quantity == expected.Quantity
        && UnitPrice == expected.UnitPrice;
}
