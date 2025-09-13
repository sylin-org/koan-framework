# Unified Relationship Response Format (v2)

## Decision: Explicit Source Mapping for Parents and Children

### Motivation
- Avoids mutating base models with artificial _-prefixed properties
- Supports multiple references to the same entity type (e.g., AuthorId, ModeratorId → User)
- Cleanly disambiguates relationship sources for both parents and children
- Enables extensible, self-documenting API responses

### Response Structure

```json
{
  "entity": { /* model properties */ },
  "parents": {
    "AuthorId": { /* author user object */ },
    "ModeratorId": { /* moderator user object */ }
  },
  "children": {
    "CommentIds": [ /* comment objects */ ],
    "TagIds": [ /* tag objects */ ]
  }
}
```

- Keys in `parents` and `children` dictionaries correspond to the source property or role.
- Each relationship is explicit and unambiguous.

### DTO Example

```csharp
public class RelationshipGraph<TEntity>
{
    public TEntity Entity { get; }
    public Dictionary<string, object?> Parents { get; }
    public Dictionary<string, object?> Children { get; }

    public RelationshipGraph(TEntity entity,
        Dictionary<string, object?>? parents = null,
        Dictionary<string, object?>? children = null)
    {
        Entity = entity;
        Parents = parents ?? new();
        Children = children ?? new();
    }
}
```

### API Example

```http
GET /api/posts/123?with=AuthorId,ModeratorId,CommentIds,TagIds
```

### Benefits
- **Model purity:** No mutation of base entity
- **Disambiguation:** Multiple relationships to same type are clear
- **Extensibility:** Easily add new relationships
- **Consistency:** Same pattern for parents and children

### Migration Guidance
- Replace `_parent`/`_children` with explicit `parents`/`children` dictionaries in new APIs
- Use DTOs or wrappers for enriched responses
- Document relationship keys and roles in API reference

### References
- See also: `parent-attribute-specification.md`, `parent-key-data-layer-migration.md`
- Supersedes: `_parent`/`_children` pattern for new endpoints

---

This format is recommended for all new Sora APIs and enrichment pipelines. Existing endpoints may continue to use `_parent`/`_children` for backward compatibility.
