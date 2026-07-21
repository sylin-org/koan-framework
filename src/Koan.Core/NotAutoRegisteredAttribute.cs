namespace Koan.Core;

/// <summary>
/// Opt-out marker for <see cref="ServiceCollectionScanExtensions.AddAllOf{TService}(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Reflection.Assembly[])"/>.
/// Apply to a concrete class that <i>implements</i> a many-impls contract (<c>IPackageSource</c>,
/// <c>IDownloadProxy</c>, …) but should <b>not</b> be DI-instantiated as part of the scan.
/// </summary>
/// <remarks>
/// <para>
/// Typical use: a parameterized source that an upstream coordinator instantiates with per-instance
/// arguments (a Source row's slug + cron, a tenant id, etc.). Those ctors take primitives DI can't
/// resolve, so a blanket <c>AddAllOf&lt;IPackageSource&gt;()</c> would pick the class up and fail
/// at service-provider validation. Mark such classes to keep the scan honest.
/// </para>
/// <para>
/// The attribute is intentionally minimal — it carries no metadata, just intent. The scanner
/// reads it via <see cref="System.Reflection.CustomAttributeExtensions"/> and skips any decorated
/// type before considering registration.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class NotAutoRegisteredAttribute : Attribute
{
}
