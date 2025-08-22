namespace S3.Mq.Sample;

public sealed class UserRegistered
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}