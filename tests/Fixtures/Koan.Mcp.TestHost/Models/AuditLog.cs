using Koan.Mcp;
using Koan.Data.Core.Model;

namespace Koan.Mcp.TestHost.Models;

// Read-only entity for TypeScript generator mutation omission test
[McpEntity(Name = "AuditLog", Description = "Read-only audit log", AllowMutations = false)]
public sealed class AuditLog : Entity<AuditLog>
{
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
}
