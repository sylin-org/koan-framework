namespace Koan.Data.Cutover;

public sealed record DefaultRouteDescriptor(
    string Source,
    string Adapter,
    string RouteIdentity,
    string ConnectionIdentity,
    long ContentGeneration);
