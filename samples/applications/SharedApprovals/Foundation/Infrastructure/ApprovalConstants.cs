namespace Example.Approvals.Foundation.Infrastructure;

public static class ApprovalConstants
{
    public const string Currency = "USD";
    public const string PolicyRoute = "api/approval-policy";
    public const string ApproveRoute = "{id}/approve";
    public const string LocalOrigin = "origin:local";

    public static class Codes
    {
        public const string InvalidRequest = "approval.invalid-request";
        public const string SubmitFirst = "approval.submit-first";
        public const string OverLimit = "approval.over-limit";
        public const string AlreadyApproved = "approval.already-approved";
    }
}
