namespace SnapVault.Configuration;

/// <summary>
/// Collection configuration options bound from appsettings.json (SnapVault:Collections).
/// </summary>
public class CollectionOptions
{
    public int MaxPhotosPerCollection { get; set; } = 2048;
}
