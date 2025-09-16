namespace Koan.Web.Extensions.Capabilities;

/// <summary>
/// Canonical capability action keys used by capability controllers and authorization.
/// </summary>
public static class CapabilityActions
{
    public static class Moderation
    {
        public const string DraftCreate = "Moderation.DraftCreate";
        public const string DraftUpdate = "Moderation.DraftUpdate";
        public const string DraftGet = "Moderation.DraftGet";
        public const string Submit = "Moderation.Submit";
        public const string Withdraw = "Moderation.Withdraw";
        public const string Queue = "Moderation.Queue";
        public const string Approve = "Moderation.Approve";
        public const string Reject = "Moderation.Reject";
        public const string Return = "Moderation.Return";
    }

    public static class SoftDelete
    {
        public const string ListDeleted = "SoftDelete.ListDeleted";
        public const string Delete = "SoftDelete.Delete";
        public const string DeleteMany = "SoftDelete.DeleteMany";
        public const string Restore = "SoftDelete.Restore";
        public const string RestoreMany = "SoftDelete.RestoreMany";
    }

    public static class Audit
    {
        public const string Snapshot = "Audit.Snapshot";
        public const string List = "Audit.List";
        public const string Revert = "Audit.Revert";
    }
}
