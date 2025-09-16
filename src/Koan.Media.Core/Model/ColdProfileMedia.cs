namespace Koan.Media.Core.Model;

using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;

[StorageBinding(Profile = "cold", Container = "cold")]
public sealed class ColdProfileMedia : MediaEntity<ColdProfileMedia>
{
}
