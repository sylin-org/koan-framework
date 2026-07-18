using System.ComponentModel.DataAnnotations;

namespace Koan.Storage.Connector.Local;

public sealed class LocalStorageOptions
{
    [Required]
    public string BasePath { get; set; } = "";
}
