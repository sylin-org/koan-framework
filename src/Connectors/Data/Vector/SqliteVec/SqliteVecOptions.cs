namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>Placement and bounded-work options for the embedded sqlite-vec adapter.</summary>
public sealed class SqliteVecOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;

    /// <summary>SQLite placement, or <c>auto</c> to pair with the selected SQLite record source.</summary>
    public string ConnectionString { get; set; } = Infrastructure.Constants.Configuration.Automatic;

    /// <summary>Maximum UTF-8 bytes accepted for one neutral metadata object.</summary>
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;

    /// <summary>Maximum exact candidates materialized when stable tie resolution requires expansion.</summary>
    public int MaxSearchCandidates { get; set; } = Infrastructure.Constants.Defaults.MaxSearchCandidates;
}
