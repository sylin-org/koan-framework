namespace Koan.Core.Ordering;

/// <summary>
/// An invalid <see cref="BeforeAttribute"/>/<see cref="AfterAttribute"/> declaration, with the module that declared it,
/// so the semantic runtime can report it as that module's registration failure (ARCH-0138).
/// </summary>
internal sealed class ModuleOrderingException(Type module, string message) : InvalidOperationException(message)
{
    public Type Module { get; } = module;
}
