namespace Koan.Web.Auth.Standing;

/// <summary>
/// What is true of a subject now. Whatever mints access for a subject long after they approved it asks this rather than
/// replaying what it captured then.
/// </summary>
/// <param name="Active">Whether the subject may act. False from any contributor is the answer, whatever others say.</param>
/// <param name="Name">Their current name, or null when this contributor has none to give.</param>
/// <param name="Roles">The roles they hold now, or null when this contributor says nothing of roles. An empty list says they hold none.</param>
public sealed record SubjectStanding(bool Active, string? Name = null, IReadOnlyCollection<string>? Roles = null);
