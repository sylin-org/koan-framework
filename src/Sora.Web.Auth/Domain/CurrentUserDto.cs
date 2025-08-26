namespace Sora.Web.Auth.Domain;

public sealed class CurrentUserDto
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? PictureUrl { get; init; }
    public IReadOnlyList<ConnectionDto> Connections { get; init; } = Array.Empty<ConnectionDto>();
}