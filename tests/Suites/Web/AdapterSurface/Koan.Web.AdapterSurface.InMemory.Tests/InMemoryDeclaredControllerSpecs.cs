using Koan.Web.AdapterSurface.TestKit.Declared;

namespace Koan.Web.AdapterSurface.InMemory.Tests;

/// <summary>WEB-0087 in memory.</summary>
public sealed class InMemoryDeclaredControllerSpecs(InMemoryAdapterFactory factory) : DeclaredControllerSpecsBase<InMemoryAdapterFactory>(factory);
