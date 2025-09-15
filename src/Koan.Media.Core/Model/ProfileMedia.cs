using Koan.Storage.Abstractions;

namespace Koan.Media.Core.Model;

using Koan.Media.Abstractions.Model;
using Storage.Infrastructure;

[StorageBinding(Profile = "default", Container = "media")]
public sealed class ProfileMedia : MediaEntity<ProfileMedia>, IStorageObject
{
    // Room for domain-specific properties if needed (e.g., Translations, etc.)
}
