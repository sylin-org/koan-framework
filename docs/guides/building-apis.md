---
type: GUIDE
domain: web
title: "Building APIs with Koan"
audience: [developers, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Building APIs with Koan

**Document Type**: GUIDE
**Target Audience**: Developers
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Basic REST API

### Entity and Controller

```csharp
// Models/Product.cs
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

// Controllers/ProductsController.cs
[Route("api/[controller]")]
public class ProductsController : EntityController<Product> { }
```

That's it. You have:
- `GET /api/products` - List all
- `GET /api/products/{id}` - Get by ID
- `POST /api/products` - Create
- `PUT /api/products/{id}` - Update
- `DELETE /api/products/{id}` - Delete

### Custom Endpoints

```csharp
[Route("api/[controller]")]
public class ProductsController : EntityController<Product>
{
    [HttpGet("featured")]
    public Task<Product[]> GetFeatured() =>
        Product.Where(p => p.IsFeatured);

    [HttpGet("category/{category}")]
    public Task<Product[]> GetByCategory(string category) =>
        Product.Where(p => p.Category == category);

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id)
    {
        var product = await Product.ById(id);
        if (product == null) return NotFound();

        product.IsActive = true;
        await product.Save();
        return Ok();
    }
}
```

## Business Logic in Entities

```csharp
public class Order : Entity<Order>
{
    public string CustomerEmail { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Business methods
    public async Task Ship()
    {
        Status = OrderStatus.Shipped;
        await Save();
        await new OrderShippedEvent { OrderId = Id }.Send();
    }

    public async Task Cancel(string reason)
    {
        if (Status == OrderStatus.Shipped)
            throw new InvalidOperationException("Cannot cancel shipped order");

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        await Save();
    }

    // Query methods
    public static Task<Order[]> ForCustomer(string email) =>
        Query().Where(o => o.CustomerEmail == email);

    public static Task<Order[]> Recent(int days = 30) =>
        Query().Where(o => o.Created > DateTimeOffset.UtcNow.AddDays(-days));
}
```

## Complex Controllers

```csharp
[Route("api/[controller]")]
public class OrdersController : EntityController<Order>
{
    [HttpPost("{id}/ship")]
    public async Task<IActionResult> Ship(string id)
    {
        var order = await Order.ById(id);
        if (order == null) return NotFound();

        try
        {
            await order.Ship();
            return Ok(new { message = "Order shipped successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("customer/{email}")]
    public Task<Order[]> GetCustomerOrders(string email) =>
        Order.ForCustomer(email);

    [HttpGet("analytics/revenue")]
    public async Task<IActionResult> GetRevenue([FromQuery] int days = 30)
    {
        var orders = await Order.Recent(days);
        var revenue = orders.Sum(o => o.Total);
        return Ok(new { revenue, orderCount = orders.Length });
    }
}
```

## Request/Response Transformation

```csharp
public class ProductTransformer : IPayloadTransformer<Product>
{
    public Task<object> TransformResponse(Product product, TransformContext context)
    {
        return Task.FromResult<object>(new
        {
            product.Id,
            product.Name,
            product.Price,
            FormattedPrice = $"${product.Price:F2}",
            Url = $"/products/{product.Id}",
            InStock = product.Quantity > 0
        });
    }
}
```

## Validation

```csharp
public class CreateProductRequest
{
    [Required]
    public string Name { get; set; } = "";

    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }

    [Required]
    public string Category { get; set; } = "";
}

[Route("api/[controller]")]
public class ProductsController : EntityController<Product>
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var product = new Product
        {
            Name = request.Name,
            Price = request.Price,
            Category = request.Category
        };

        await product.Save();
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }
}
```

## File Uploads

```csharp
[Route("api/[controller]")]
public class ProductsController : EntityController<Product>
{
    [HttpPost("{id}/image")]
    public async Task<IActionResult> UploadImage(string id, IFormFile file)
    {
        var product = await Product.ById(id);
        if (product == null) return NotFound();

        var image = await ProductImage.UploadAsync(file);
        product.ImageId = image.Id;
        await product.Save();

        return Ok(new { imageUrl = $"/media/{image.Id}" });
    }
}
```

## Authentication

```csharp
[Route("api/[controller]")]
[Authorize]
public class OrdersController : EntityController<Order>
{
    [HttpGet]
    public Task<Order[]> GetMyOrders()
    {
        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        return Order.ForCustomer(userEmail);
    }

    [HttpPost]
    [Authorize(Policy = "CanCreateOrders")]
    public override Task<IActionResult> Post([FromBody] Order entity)
    {
        entity.CustomerEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        return base.Post(entity);
    }
}
```

## Error Handling

```csharp
[Route("api/[controller]")]
public class OrdersController : EntityController<Order>
{
    [HttpPost("{id}/refund")]
    public async Task<IActionResult> Refund(string id, [FromBody] RefundRequest request)
    {
        try
        {
            var order = await Order.ById(id);
            if (order == null) return NotFound();

            await order.ProcessRefund(request.Amount, request.Reason);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InsufficientFundsException ex)
        {
            return BadRequest(new { error = "Refund amount exceeds order total" });
        }
    }
}
```

## Testing

```csharp
[Test]
public async Task Should_Create_Product()
{
    // Arrange
    var controller = new ProductsController();
    var request = new CreateProductRequest
    {
        Name = "Test Product",
        Price = 99.99m,
        Category = "Electronics"
    };

    // Act
    var result = await controller.Create(request);

    // Assert
    Assert.IsInstanceOf<CreatedAtActionResult>(result);
    var products = await Product.All();
    Assert.AreEqual(1, products.Length);
    Assert.AreEqual("Test Product", products[0].Name);
}
```

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+