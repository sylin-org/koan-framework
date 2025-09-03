namespace Sora.Messaging.Primitives;

/// <summary>
/// Marker interface for a Command primitive (directed work; point-to-point semantics).
/// </summary>
public interface ICommandPrimitive { }

/// <summary>
/// Marker interface for an Announcement primitive (broadcast / fan-out semantics).
/// </summary>
public interface IAnnouncementPrimitive { }

/// <summary>
/// Marker interface for a FlowEvent primitive (adapter emission -> flow ingestion).
/// </summary>
public interface IFlowEventPrimitive { }
