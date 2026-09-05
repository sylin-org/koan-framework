using System.ComponentModel.DataAnnotations;
using Koan.Data.Core.Model;

namespace Example.Approvals.Foundation.Domain;

public abstract class ApprovalRequest<TEntity> : Entity<TEntity>
    where TEntity : ApprovalRequest<TEntity>, new()
{
    [Required]
    public string Subject { get; set; } = "";
    public decimal Amount { get; set; }
    public ApprovalState State { get; set; }
}
