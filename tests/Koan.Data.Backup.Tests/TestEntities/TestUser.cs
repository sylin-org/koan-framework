using Koan.Data.Abstractions;

namespace Koan.Data.Backup.Tests.TestEntities;

public class TestUser : IEntity<Guid>
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public int Age { get; set; }
    public string[]? Tags { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class TestProduct : IEntity<string>
{
    public string Id { get; set; } = Guid.CreateVersion7().ToString();
    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public string Category { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool InStock { get; set; } = true;
    public int Quantity { get; set; }
}

public class TestOrder : IEntity<long>
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public decimal Total { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending";
    public List<string> ProductIds { get; set; } = new();
}