namespace Sora.Media.Core.Model;

using Sora.Media.Abstractions.Model;
using Sora.Storage.Infrastructure;

[StorageBinding(Profile = "cold", Container = "cold")]
public sealed class ColdProfileMedia : MediaEntity<ColdProfileMedia>
{
}
