namespace Koan.Web.Hooks;

/// <summary>
/// Hooks can opt into ordering to control execution precedence; one that says nothing runs at 0.
/// </summary>
public interface IOrderedHook { int Order => 0; }
