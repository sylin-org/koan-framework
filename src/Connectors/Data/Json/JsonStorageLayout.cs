namespace Koan.Data.Connector.Json;

/// <summary>Controls whether a JSON source groups an Entity set or persists each Entity independently.</summary>
public enum JsonStorageLayout
{
    /// <summary>Persist the Entity set as one JSON array file.</summary>
    Aggregate,

    /// <summary>Persist each Entity as its own JSON object file.</summary>
    IndividualFiles
}
