using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace S0.ConsoleJsonRepo;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
    public bool Done { get; set; }

    public async Task Complete(CancellationToken ct = default)
    {
        Done = true;
        await this.Save(ct);
    }
}
