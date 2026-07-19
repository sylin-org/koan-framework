namespace Koan.Orchestration.Abstractions;

public sealed record PortBinding(string Service, int Host, int Container, string Protocol = "tcp", string? Address = null);