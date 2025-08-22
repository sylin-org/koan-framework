namespace Sora.Messaging;

public sealed class RetryOptions
{
    public int MaxAttempts { get; set; } = 5;
    public string Backoff { get; set; } = "exponential"; // or fixed
    public int FirstDelaySeconds { get; set; } = 3;
    public int MaxDelaySeconds { get; set; } = 60;
}