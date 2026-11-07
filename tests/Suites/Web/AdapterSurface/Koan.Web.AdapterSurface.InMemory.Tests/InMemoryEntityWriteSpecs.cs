using Koan.Web.AdapterSurface.TestKit.EntityWrites;

namespace Koan.Web.AdapterSurface.InMemory.Tests;

/// <summary>WEB-0086 in memory, which holds no transaction: a write runs as it does without one, and refuses the same way.</summary>
public sealed class InMemoryEntityWriteSpecs(InMemoryAdapterFactory factory) : EntityWriteSpecsBase<InMemoryAdapterFactory>(factory)
{
    protected override bool Holds => false;
}
