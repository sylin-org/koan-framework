namespace Koan.Samples.Meridian.Infrastructure;

public sealed class DeliverableStorageOptions
{
    public string Profile { get; set; } = MeridianConstants.StorageProfile;
    public string Container { get; set; } = MeridianConstants.StorageContainer;
    public string Prefix { get; set; } = "deliverables/";
}
