namespace Sora.Web.Extensions.Moderation;

public readonly record struct FlowResult(bool Ok, int? Status, string? Code, string? Message)
{
    public static FlowResult Success => new(true, null, null, null);
    public static FlowResult Fail(int status, string code, string message) => new(false, status, code, message);
}