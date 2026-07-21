namespace Koan.Communication;

/// <summary>Declares that an Entity Event contract requires explicit business details.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EventDetailsRequiredAttribute : Attribute;
