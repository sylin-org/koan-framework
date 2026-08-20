using System.ComponentModel.DataAnnotations;
using Koan.Storage.Connector.Local.Infrastructure;

namespace Koan.Storage.Connector.Local;

public sealed class LocalStorageOptions
{
    /// <summary>
    /// Directory that holds stored bytes, relative to the content root unless rooted. Defaults to
    /// <see cref="LocalStorageConstants.DefaultBasePath"/> so a bare reference composes. Explicitly
    /// configuring an empty path still fails validation, because that states an intent the connector
    /// cannot honor.
    /// </summary>
    [Required]
    public string BasePath { get; set; } = LocalStorageConstants.DefaultBasePath;
}
