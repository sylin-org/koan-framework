namespace Koan.Core;

/// <summary>
/// Marks an <b>interface</b> whose concrete implementers should be auto-discovered into
/// <c>KoanRegistry</c> at boot — build-time via the source generator, runtime-fallback via
/// <c>RegistryManifestLoader</c> — and queried with <c>KoanRegistry.GetDiscoveredImplementors(typeof(T))</c>.
/// <para>
/// This replaces bespoke <c>AppDomain.CurrentDomain.GetAssemblies()</c> reflection scans (which miss
/// lazily-loaded Koan assemblies and bypass the single discovery authority) with the same fast,
/// fail-safe discovery every other Koan contract uses. ARCH-0086.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class KoanDiscoverableAttribute : Attribute
{
}
