using Sora.Storage.Abstractions;

namespace Sora.Media.Core.Model;

using Sora.Media.Abstractions.Model;
using Storage.Infrastructure;

[StorageBinding(Profile = "default", Container = "media")]
public sealed class ProfileMedia : MediaEntity<ProfileMedia>, IStorageObject
{
    // Room for domain-specific properties if needed (e.g., Translations, etc.)
}
