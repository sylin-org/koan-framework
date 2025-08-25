namespace Sora.Media.Core.Model;

using Sora.Media.Abstractions.Model;
using Sora.Storage.Infrastructure;

[StorageBinding(Profile = "default", Container = "media")]
public sealed class ProfileMedia : MediaEntity<ProfileMedia>
{
    // Room for domain-specific properties if needed (e.g., Translations, etc.)
}
