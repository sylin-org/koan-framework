namespace Koan.Data.Core;

/// <summary>A provider/source pair selected by a runtime data operation in one Koan host.</summary>
public sealed record DataAdapterParticipationInfo(string Provider, string Source);
