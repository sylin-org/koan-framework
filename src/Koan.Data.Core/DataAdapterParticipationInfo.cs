namespace Koan.Data.Core;

/// <summary>A provider/source pair selected by a runtime data operation in one Koan host.</summary>
public sealed record DataAdapterParticipationInfo(
    string Provider,
    string Source,
    DataAdapterParticipationRole Role = DataAdapterParticipationRole.Explicit)
{
    /// <summary>Preserves the original provider/source binary contract.</summary>
    public DataAdapterParticipationInfo(string Provider, string Source)
        : this(Provider, Source, DataAdapterParticipationRole.Explicit)
    {
    }

    /// <summary>Preserves the original two-value positional shape.</summary>
    public void Deconstruct(out string Provider, out string Source)
    {
        Provider = this.Provider;
        Source = this.Source;
    }
}
