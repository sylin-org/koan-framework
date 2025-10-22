# Collection Management - Architectural Decisions

**Date**: 2025-01-18
**Status**: Approved for Implementation

## Executive Summary

This document records the architectural decisions made for the S6.SnapVault collection management feature implementation.

---

## Core Feature: Text-Selection Drag UX

**Decision**: Implement revolutionary text-selection drag pattern for desktop power users.

**Pattern**: Users select photos by dragging cursor across grid (like selecting text), then drag to collections/favorites.

**Benefits**:
- 50-66% reduction in interaction steps vs traditional Shift+Click paradigm
- Leverages muscle memory from text editing
- Native browser selection behavior
- No modifier keys required

---

## Technical Decisions

### 1. Text Selection Scoping

**Decision**: Scope selection tracking strictly to mousedown events starting on `.photo-grid`

**Rationale**:
- Prevents conflicts with text selection in sidebar, headers, metadata
- No need to disable user-select on UI chrome (keeps accessibility)
- Clean separation of drag contexts

**Implementation**:
```javascript
gridContainer.addEventListener('mousedown', (e) => {
    const photoItem = e.target.closest('.photo-item');
    if (!photoItem) return;
    selectionStarted = true;
    // ... selection logic
});
```

---

### 2. Collection Size Limit

**Decision**: 2,048 photos maximum per collection (configurable)

**Configuration Location**: `appsettings.json`
```json
{
  "SnapVault": {
    "Collections": {
      "MaxPhotosPerCollection": 2048
    }
  }
}
```

**Rationale**:
- Keeps `PhotoIds` arrays manageable across all providers
- Prevents performance issues with large collections
- Configurable for future adjustment

**UI Feedback**: Progress bar showing capacity usage

---

### 3. Photo Ordering in Collections

**Decision**: Use list index as position (Option A)

**Pattern**:
```csharp
public class Collection : Entity<Collection> {
    public List<string> PhotoIds { get; set; } = new();
    // Index in PhotoIds array = display position
}
```

**Rationale**:
- Simple, no additional storage needed
- Array semantics naturally handle gaps when removing
- Easy drag-to-reorder implementation: `PhotoIds.RemoveAt(oldIndex); PhotoIds.Insert(newIndex, photoId);`

**Alternatives Rejected**:
- Option B (Dictionary<string, int>): Redundant, can drift out of sync
- Option C (PhotoPosition class): Overengineered for current needs

---

### 4. Delete Key Behavior

**Decision**: Delete key in collection view = Remove from collection (non-destructive)

**Behavior**:
- **Collection View**: Delete key removes photo from collection (toast notification)
- **All Photos View**: Delete key permanently deletes photo (confirmation dialog)

**Rationale**:
- Aligns with "reference-based" core principle
- Safe by default (prevents accidental permanent deletion)
- Consistent with "only All Photos allows permanent delete"

---

### 5. Entity Lifecycle Pattern

**Decision**: Confirm BeforeRemove pattern as canonical

**Pattern**:
```csharp
PhotoAsset.Events.BeforeRemove(async ctx => {
    // Delete storage files for thumbnails
    await thumbnail.Delete(ct);  // Storage cleanup
    await thumbnail.Remove(ct);  // Entity deletion

    // Delete main photo storage
    await photo.Delete(ct);

    // Entity removal proceeds after event
    return EntityEventResult.Proceed();
});
```

**Critical Distinction**:
- `Delete()`: Storage file deletion (StorageEntity method)
- `Remove()`: Entity deletion + lifecycle events (Entity method)
- Main photo uses `Delete()` for storage in event, then `Remove()` completes entity deletion

---

### 6. Frontend Architecture

**Decision**: Vanilla JavaScript (matches existing architecture)

**Existing Pattern**:
- ES6 modules (`import`/`export`)
- Class-based components
- Centralized state in `SnapVaultApp.state`
- Component communication through `this.app`
- Pure DOM manipulation

**Rationale**:
- Zero migration risk
- Consistent with 20+ existing components
- Modern, performant, maintainable
- No framework overhead
- Developers already familiar with patterns

**Alternatives Rejected**:
- AngularJS 1.x: EOL (no security patches), migration overhead
- Alpine.js/Vue: Migration risk, breaking changes to existing components
- React: Build step complexity, architecture mismatch

---

### 7. User Ownership

**Decision**: Global collections (no user context)

**Current**: Single-tenant assumption for sample app

**Future Migration Path**:
```csharp
// Phase 1: Add property
public class Collection : Entity<Collection> {
    public string? UserId { get; set; }  // Nullable for migration
}

// Phase 2: Update queries
var collections = await Collection.Query(c => c.UserId == currentUserId);

// Phase 3: Data migration
// UPDATE collections SET UserId = 'default-user' WHERE UserId IS NULL
```

**Rationale**: Simplifies sample, planned evolution documented

---

### 8. Request Model Organization

**Decision**: Define request DTOs inline in controller

**Pattern**:
```csharp
// At end of CollectionsController.cs
#region Request Models
public class CreateCollectionRequest { ... }
public class UpdateCollectionRequest { ... }
#endregion
```

**Rationale**:
- Reduces file sprawl for small DTOs
- Keeps related code together
- Standard ASP.NET Core practice for simple APIs

**Alternative**: Separate `Models/Requests/` folder (useful when DTOs become large or shared)

---

## Data Model

### Collection Entity

```csharp
public class Collection : Entity<Collection>
{
    public string Name { get; set; } = "";
    public List<string> PhotoIds { get; set; } = new();  // Active Record pattern
    public string? CoverPhotoId { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int PhotoCount => PhotoIds.Count;
}
```

**Key Design Choices**:
- `List<string> PhotoIds`: Active Record pattern (no junction table)
- List index = photo display position
- `SortOrder`: Collection display order in sidebar
- `CoverPhotoId`: Optional thumbnail (fallback to first photo)

### PhotoAsset Modification

```csharp
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    // ... existing properties ...

    public bool IsFavorite { get; set; }  // Enables Favorites system view
}
```

**Rationale**: Simple boolean flag instead of separate Favorites collection entity

---

## Deletion Semantics

| Action | Context | Effect | Confirmation |
|--------|---------|--------|--------------|
| Remove from Collection | Collection View | Photo ID removed from `PhotoIds` array | Toast only (non-destructive) |
| Delete Collection | Sidebar | Collection entity removed | Dialog ("X photos remain in library") |
| Unfavorite | Any View | `IsFavorite = false` | None (non-destructive) |
| Permanent Delete | **All Photos Only** | Entity + storage + thumbnails deleted | Strong warning dialog |

**Core Principle**: Reference-based = safe by default. Only "All Photos" view allows destructive operations.

---

## API Design

### Collections Endpoints

```
GET    /api/collections              # List all collections (sorted by SortOrder)
GET    /api/collections/{id}         # Get single collection metadata
GET    /api/collections/{id}/photos  # Get photos in collection (paginated)
POST   /api/collections              # Create new collection
PUT    /api/collections/{id}         # Update metadata (name, cover, sort)
POST   /api/collections/{id}/photos  # Add photos to collection
POST   /api/collections/{id}/photos/remove  # Remove photos (non-destructive)
DELETE /api/collections/{id}         # Delete collection (photos remain)
```

### Favorites Endpoints

```
PUT    /api/photos/{id}/favorite     # Toggle favorite status
GET    /api/photos/favorites         # Get all favorited photos
```

**Validation**: All endpoints include 2,048 photo limit enforcement

---

## UI Components (Vanilla JS)

### Component Architecture

```javascript
// New components following existing pattern
import { CollectionsSidebar } from './components/collectionsSidebar.js';
import { PhotoSelection } from './components/photoSelection.js';
import { DragDropManager } from './components/dragDropManager.js';
import { CollectionView } from './components/collectionView.js';

// Integration in SnapVaultApp
this.components.collectionsSidebar = new CollectionsSidebar(this);
this.components.photoSelection = new PhotoSelection(this);
this.components.dragDrop = new DragDropManager(this);
this.components.collectionView = new CollectionView(this);
```

### State Management

```javascript
// Centralized in app.state
this.state = {
    collections: [],           // All user collections
    activeView: 'all-photos',  // 'all-photos' | 'favorites' | collectionId
    selectedPhotos: new Set(), // Currently selected photo IDs
    // ... existing state ...
};
```

---

## Implementation Sequence

### Phase 1: Backend Foundation
1. Configuration in `appsettings.json`
2. Collection entity
3. PhotoAsset.IsFavorite property
4. CollectionsController with validation
5. PhotosController favorites endpoints
6. Verify cascade delete pattern

### Phase 2: Vanilla JS Components
1. CollectionsSidebar (system views + user collections)
2. PhotoSelection (text-selection drag logic)
3. DragDropManager (drop zones, visual feedback)
4. CollectionView (header, actions, progress bar)

### Phase 3: Integration & CSS
1. Wire components into app.js
2. Add collection-specific CSS
3. Keyboard shortcuts (Delete key handling)
4. Toast notifications

### Phase 4: Testing
1. All interaction flows from spec
2. Deletion semantics verification
3. Edge cases (limits, conflicts, errors)
4. Performance with 2K photos

---

## Koan Framework Patterns Applied

### Entity-First Development
```csharp
// ✅ Correct: Use entity static methods
var collection = await Collection.Get(id);
await collection.Save();

// ❌ Wrong: Manual repository injection
// private readonly IRepository<Collection> _repo;
```

### Active Record Relationships
```csharp
// ✅ Correct: ID array (many-to-many)
public List<string> PhotoIds { get; set; } = new();

// ❌ Wrong: Junction table pattern
// public class CollectionPhoto { ... }
```

### Save() vs UpsertAsync()
```csharp
using Koan.Data.Core;  // Required for Save() extension

await collection.Save();  // ✅ Canonical pattern
```

### Delete() vs Remove() Semantics
```csharp
await photo.Delete(ct);  // Storage file only
await photo.Remove(ct);  // Entity + cascade events
```

---

## Performance Considerations

### Large Collections
- **Pagination**: `GET /api/collections/{id}/photos?skip=100&take=100`
- **Limit enforcement**: 2,048 photos maximum
- **Progress bar**: Visual capacity feedback

### Query Optimization
- **Provider detection**: Automatic fallback if `Contains()` not supported
- **Batch operations**: Single `Save()` for multiple photo additions
- **Lazy loading**: Collections load metadata only, photos on demand

---

## Future Enhancements (Not in MVP)

1. Collection hierarchy (nested collections)
2. Smart collections (dynamic queries)
3. Collection sharing (export/import)
4. Bulk photo operations (select all, move)
5. Auto-cover selection (most recent/favorited)
6. Keyboard shortcuts (Cmd+N for new collection)
7. Drag-to-reorder collections (SortOrder)
8. Collection color coding

---

## References

- **Full Specification**: `COLLECTION_MANAGEMENT_IMPLEMENTATION.md`
- **Koan Framework Patterns**: Project `CLAUDE.md`
- **Existing Components**: `wwwroot/js/components/grid.js`, `app.js`
- **Entity Patterns**: `Models/PhotoAsset.cs`, `Models/Media.cs`

---

## Sign-Off

**Decisions Finalized**: 2025-01-18
**Implementation Ready**: ✅
**Breaking Changes**: None (additive only)
**Migration Required**: None (new feature)
