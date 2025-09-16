namespace Koan.Web.Hooks;

/// <summary>
/// Hooks can opt into ordering to control execution precedence.
/// </summary>
public interface IOrderedHook { int Order { get; } }