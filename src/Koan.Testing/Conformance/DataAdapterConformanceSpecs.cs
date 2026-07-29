using Koan.Testing.Conformance.Infrastructure;
using Xunit;

namespace Koan.Testing;

/// <summary>
/// Inherit once per adapter to execute every stable primer acceptance ID against one packet. Missing evidence,
/// deferred/infrastructure outcomes, and declined paths without corrective proof remain non-green.
/// </summary>
public abstract class DataAdapterConformanceSpecs
{
    /// <summary>The immutable packet produced for the adapter and pinned provider fixture.</summary>
    protected abstract DataConformancePacket Packet { get; }

    /// <summary>The generated acceptance theory data. The primer remains the source of these IDs.</summary>
    public static IEnumerable<object[]> AcceptanceIds() =>
        DataConformanceCatalog.Cells.Select(cell => new object[] { cell.Id });

    [Theory(DisplayName = "Data adapter primer cell has complete evidence")]
    [MemberData(nameof(AcceptanceIds))]
    [Trait(DataConformanceConstants.CategoryTrait, DataConformanceConstants.Category)]
    public void Acceptance_cell_has_complete_evidence(string acceptanceId)
    {
        var result = Packet.ValidateAcceptance(acceptanceId);
        Assert.True(result.IsValid,
            $"{acceptanceId} is {result.Status}: {string.Join(" | ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}"))}");
    }
}
