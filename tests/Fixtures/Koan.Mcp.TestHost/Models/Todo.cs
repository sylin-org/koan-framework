using Koan.Mcp;
using Koan.Data.Core.Model;

namespace Koan.Mcp.TestHost.Models;

[McpEntity(Name = "Todo", Description = "Test todo entity", Exposure = McpExposureMode.Full)]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
    public bool Completed { get; set; }
}