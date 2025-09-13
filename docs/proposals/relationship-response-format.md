# Enhanced Relationship Response Format
**Using _parent and _children Structure**

## Response Format Design

### Core Principle
Relationship data is separated from entity data using reserved `_parent` and `_children` keys. This keeps the core entity properties clean while providing rich relationship information.

### Single Parent Example
```json
{
  "id": "123",
  "name": "Product A",
  "price": 29.99,
  "categoryId": "c01",
  "_parent": {
    "category": {
      "id": "c01",
      "name": "Electronics",
      "description": "Electronic products"
    }
  }
}
```

### Multiple Parent Example
```json
{
  "id": "123",
  "total": 299.99,
  "status": "shipped",
  "customerId": "456",
  "categoryId": "c01",
  "_parent": {
    "customer": {
      "id": "456", 
      "name": "John Doe",
      "email": "john@example.com"
    },
    "category": {
      "id": "c01",
      "name": "Category 01",
      "description": "Product Category"
    }
  }
}
```

### Children Example
```json
{
  "id": "456",
  "name": "John Doe",
  "email": "john@example.com",
  "_children": {
    "orders": [
      {
        "id": "123",
        "total": 299.99,
        "status": "shipped"
      },
      {
        "id": "124", 
        "total": 149.50,
        "status": "pending"
      }
    ]
  }
}
```

### Combined Parent and Children
```json
{
  "id": "o123",
  "total": 299.99,
  "customerId": "456",
  "_parent": {
    "customer": {
      "id": "456",
      "name": "John Doe" 
    }
  },
  "_children": {
    "items": [
      {
        "id": "789",
        "name": "Product A",
        "quantity": 2,
        "price": 149.99
      }
    ],
    "payments": [
      {
        "id": "p001", 
        "amount": 299.99,
        "method": "credit_card"
      }
    ]
  }
}
```

## Query Parameter Examples

### Basic Relationship Loading
```http
# Load single parent
GET /api/products/123?with=category

# Load multiple parents  
GET /api/orders/123?with=customer,category

# Load children
GET /api/customers/456?with=orders

# Load specific child types
GET /api/orders/123?with=items,payments

# Combined parent and children
GET /api/orders/123?with=customer,items,payments
```

### Advanced Query Examples
```http
# JSON array format
GET /api/orders/123?with=["customer","items","payments"]

# Nested relationships (future enhancement)
GET /api/orders/123?with=customer.addresses,items.product

# Filtered relationships (future enhancement) 
GET /api/customers/456?with=orders&orders.filter={"status":"active"}
```

## Implementation in EntityController

### Enhanced Response Wrapper
```csharp
public class EntityWithRelationships<TEntity>
{
    public TEntity Entity { get; set; } = default!;
    public Dictionary<string, object>? _parent { get; set; }
    public Dictionary<string, object>? _children { get; set; }
    
    // Custom JSON serialization to flatten the structure
    public void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        
        // Write entity properties
        var entityJson = JsonSerializer.SerializeToElement(Entity, options);
        foreach (var property in entityJson.EnumerateObject())
        {
            property.WriteTo(writer);
        }
        
        // Write relationships
        if (_parent?.Any() == true)
        {
            writer.WritePropertyName("_parent");
            JsonSerializer.Serialize(writer, _parent, options);
        }
        
        if (_children?.Any() == true)
        {
            writer.WritePropertyName("_children");
            JsonSerializer.Serialize(writer, _children, options);
        }
        
        writer.WriteEndObject();
    }
}
```

### Updated EntityController LoadRelationships Method
```csharp
private async Task<object> LoadRelationshipsAsync<TEntity, TKey>(
    TEntity entity, 
    List<string> relationships, 
    CancellationToken ct)
    where TEntity : class, IEntity<TKey>
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var parentData = new Dictionary<string, object>();
    var childrenData = new Dictionary<string, object>();
    
    foreach (var relationshipName in relationships)
    {
        try
        {
            if (await IsParentRelationship<TEntity>(relationshipName))
            {
                var parentEntity = await LoadParentByName<TEntity, TKey>(entity.Id, relationshipName, ct);
                if (parentEntity != null)
                {
                    parentData[relationshipName] = parentEntity;
                }
            }
            else if (await IsChildRelationship<TEntity>(relationshipName))
            {
                var childEntities = await LoadChildrenByName<TEntity, TKey>(entity.Id, relationshipName, ct);
                if (childEntities?.Any() == true)
                {
                    childrenData[relationshipName] = childEntities;
                }
            }
        }
        catch (Exception ex)
        {
            var logger = HttpContext.RequestServices.GetService<ILogger<EntityController<TEntity, TKey>>>();
            logger?.LogWarning(ex, "Failed to load relationship {Relationship} for entity {EntityId}", 
                relationshipName, entity.Id);
        }
    }
    
    // Create response with relationships
    var response = new Dictionary<string, object>();
    
    // Add all entity properties
    var entityJson = JsonSerializer.SerializeToElement(entity);
    foreach (var property in entityJson.EnumerateObject())
    {
        response[property.Name] = property.Value.Clone();
    }
    
    // Add relationship data
    if (parentData.Any())
    {
        response["_parent"] = parentData;
    }
    
    if (childrenData.Any())
    {
        response["_children"] = childrenData;
    }
    
    return response;
}

private async Task<bool> IsParentRelationship<TEntity>(string relationshipName)
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var parentRelations = relationshipMetadata.GetParentRelationships(typeof(TEntity));
    
    return parentRelations.Any(r => 
        string.Equals(GetRelationshipName(r.ParentType), relationshipName, StringComparison.OrdinalIgnoreCase));
}

private async Task<bool> IsChildRelationship<TEntity>(string relationshipName)
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var childRelations = relationshipMetadata.GetChildRelationships(typeof(TEntity));
    
    return childRelations.Any(r => 
        string.Equals(GetRelationshipName(r.EntityType), relationshipName, StringComparison.OrdinalIgnoreCase));
}

private async Task<object?> LoadParentByName<TEntity, TKey>(
    TKey entityId, 
    string parentName, 
    CancellationToken ct)
    where TEntity : class, IEntity<TKey>
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var parentRelations = relationshipMetadata.GetParentRelationships(typeof(TEntity));
    
    var parentRelation = parentRelations.FirstOrDefault(r => 
        string.Equals(GetRelationshipName(r.ParentType), parentName, StringComparison.OrdinalIgnoreCase));
    
    if (parentRelation == null) return null;
    
    // Get the entity to extract the parent ID
    var entity = await Data<TEntity, TKey>.GetAsync(entityId, ct);
    if (entity == null) return null;
    
    // Extract parent ID from entity using reflection
    var parentIdProperty = typeof(TEntity).GetProperty(parentRelation.ForeignKeyProperty, 
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    
    if (parentIdProperty == null) return null;
    
    var parentId = parentIdProperty.GetValue(entity);
    if (parentId == null) return null;
    
    // Load parent entity using reflection
    var parentDataMethod = typeof(Data<,>).MakeGenericType(parentRelation.ParentType, typeof(TKey))
                                           .GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static);
    
    if (parentDataMethod == null) return null;
    
    var parentTask = (Task)parentDataMethod.Invoke(null, new object[] { parentId, ct })!;
    await parentTask;
    
    return GetTaskResult(parentTask);
}

private async Task<IEnumerable<object>?> LoadChildrenByName<TEntity, TKey>(
    TKey entityId,
    string childName,
    CancellationToken ct)
    where TEntity : class, IEntity<TKey>
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var childRelations = relationshipMetadata.GetChildRelationships(typeof(TEntity));
    
    var childRelation = childRelations.FirstOrDefault(r => 
        string.Equals(GetRelationshipName(r.EntityType), childName, StringComparison.OrdinalIgnoreCase));
    
    if (childRelation == null) return null;
    
    // Query child entities using reflection
    var childDataType = typeof(Data<,>).MakeGenericType(childRelation.EntityType, typeof(TKey));
    var queryMethod = childDataType.GetMethod("Query", BindingFlags.Public | BindingFlags.Static);
    
    if (queryMethod == null) return null;
    
    var queryable = queryMethod.Invoke(null, null);
    
    // Build WHERE clause for foreign key matching
    var parameter = Expression.Parameter(childRelation.EntityType, "x");
    var property = Expression.Property(parameter, childRelation.ForeignKeyProperty);
    var constant = Expression.Constant(entityId);
    var equal = Expression.Equal(property, constant);
    var lambda = Expression.Lambda(equal, parameter);
    
    // Apply WHERE clause
    var whereMethod = typeof(Queryable).GetMethods()
                                      .First(m => m.Name == "Where" && m.GetParameters().Length == 2)
                                      .MakeGenericMethod(childRelation.EntityType);
    
    var filteredQuery = whereMethod.Invoke(null, new[] { queryable, lambda });
    
    // Execute ToListAsync
    var toListMethod = typeof(EntityFrameworkQueryableExtensions)
                      .GetMethod("ToListAsync", BindingFlags.Public | BindingFlags.Static)!
                      .MakeGenericMethod(childRelation.EntityType);
    
    var listTask = (Task)toListMethod.Invoke(null, new object[] { filteredQuery, ct })!;
    await listTask;
    
    var result = GetTaskResult(listTask);
    return result as IEnumerable<object>;
}

private static string GetRelationshipName(Type type)
{
    // Convert type name to lowercase relationship name
    // e.g., "Customer" -> "customer", "OrderItem" -> "orderitem" or "items"
    var name = type.Name.ToLowerInvariant();
    
    // Handle common pluralization patterns for child relationships
    if (name.EndsWith("y"))
        return name.Substring(0, name.Length - 1) + "ies";
    if (name.EndsWith("s") || name.EndsWith("x") || name.EndsWith("ch") || name.EndsWith("sh"))
        return name + "es";
    
    return name + "s"; // Default pluralization for children
}

private static object? GetTaskResult(Task task)
{
    var taskType = task.GetType();
    if (taskType.IsGenericType)
    {
        return taskType.GetProperty("Result")?.GetValue(task);
    }
    return null;
}
```

## Benefits of _parent/_children Structure

### 1. **Clean Separation**
- Core entity data remains unchanged
- Relationship data is clearly distinguished
- No naming conflicts between entity properties and relationships

### 2. **Multiple Relationships**
- Supports multiple parents naturally: `_parent.customer`, `_parent.category`
- Supports multiple child types: `_children.items`, `_children.payments`
- Easy to extend without breaking existing structure

### 3. **API Evolution**
- Adding new relationships doesn't affect existing entity structure
- Backward compatibility maintained for clients not using relationships
- Optional loading - relationships only included when requested

### 4. **Developer Experience**
- Intuitive structure that's self-documenting
- Easy to understand which data is core vs. relationship
- Consistent pattern across all entities

### 5. **Performance**
- Relationships loaded only when requested via `?with=` parameter
- Batch loading possible for multiple relationships
- Caching can be applied separately to entity vs. relationship data

This structure provides a clean, extensible foundation for the unified relationship system while maintaining clarity and performance.