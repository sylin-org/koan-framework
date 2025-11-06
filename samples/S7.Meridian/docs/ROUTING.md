# Meridian Routing Implementation

**Last Updated**: 2025-10-23
**Feature**: URL Preservation with Hash-based Routing

---

## Overview

Meridian uses hash-based routing to enable:
- **Bookmarkable URLs** - Users can bookmark specific pages
- **Browser back/forward** - Native browser navigation works correctly
- **Shareable links** - URLs can be shared and will load the correct view
- **Deep linking** - Direct access to specific types, analyses, or views

## URL Structure

All routes use hash-based navigation with the pattern: `#/path/with/params`

### Route Examples

```
Root/Dashboard:
  #/                    → Dashboard
  #/dashboard           → Dashboard

Analyses:
  #/analyses            → Analyses list
  #/analyses/:id        → Analysis workspace for specific pipeline

Analysis Types:
  #/analysis-types                → Analysis types list
  #/analysis-types/create         → Create new analysis type
  #/analysis-types/:id/view       → View analysis type (read-only)
  #/analysis-types/:id/edit       → Edit analysis type

Source Types:
  #/source-types                  → Source types list
  #/source-types/create           → Create new source type
  #/source-types/:id/view         → View source type (read-only)
  #/source-types/:id/edit         → Edit source type

Legacy:
  #/manage-types        → Old types management view
  #/new-analysis        → Create new analysis wizard
  #/new-type            → Create new type manually
  #/new-type-ai         → AI Create type modal
```

## Architecture

### Router Class (`utils/Router.js`)

The Router provides:
- **Route Registration** - Define route patterns with parameter support
- **Pattern Matching** - Convert `:param` syntax to regex matching
- **Parameter Extraction** - Automatically extract route and query parameters
- **History Management** - Integrate with browser history API
- **Hash Change Listening** - Respond to back/forward navigation

#### Key Methods

```javascript
// Define a route
router.route('analysis-types/:id/view', (params) => {
  // params.id contains the extracted ID
  app.navigate('analysis-type-view', params);
});

// Navigate programmatically
router.navigate('analysis-types/abc123/edit', { source: 'dashboard' });

// Build URL for a route
const url = router.buildUrl('analysis-types/:id/view', { id: 'abc123' });
// Returns: #/analysis-types/abc123/view

// Check if route is active
if (router.isActive('analysis-types/:id/view')) {
  // Highlight nav item, etc.
}
```

### Integration with App.js

#### 1. Route Setup (`setupRoutes()`)

All application routes are registered in the `setupRoutes()` method:

```javascript
setupRoutes() {
  // Dashboard
  this.router.route('', (params) => this.navigate('dashboard', params));
  this.router.route('dashboard', (params) => this.navigate('dashboard', params));

  // Analysis Types
  this.router.route('analysis-types', (params) => this.navigate('analysis-types-list', params));
  this.router.route('analysis-types/create', (params) => this.navigate('analysis-type-create', params));
  this.router.route('analysis-types/:id/view', (params) => this.navigate('analysis-type-view', params));
  this.router.route('analysis-types/:id/edit', (params) => this.navigate('analysis-type-edit', params));

  // ... more routes
}
```

#### 2. EventBus Integration (`setupNavigation()`)

Components emit navigation events, which are converted to router paths:

```javascript
setupNavigation() {
  this.eventBus.on('navigate', (view, params = {}) => {
    // Convert view name to router path
    const routePath = this.viewToRoutePath(view, params);

    // Navigate using router (updates URL and triggers route handler)
    this.router.navigate(routePath, params);
  });
}
```

#### 3. View to Path Mapping (`viewToRoutePath()`)

Internal view names are mapped to external URL paths:

```javascript
viewToRoutePath(view, params = {}) {
  const viewToPath = {
    'dashboard': '',
    'analysis-types-list': 'analysis-types',
    'analysis-type-view': params.id ? `analysis-types/${params.id}/view` : 'analysis-types',
    'analysis-type-edit': params.id ? `analysis-types/${params.id}/edit` : 'analysis-types',
    // ... more mappings
  };

  return viewToPath[view] || view;
}
```

#### 4. Router Initialization (`init()`)

The router starts listening for hash changes on app initialization:

```javascript
async init() {
  // Setup navigation event listeners
  this.setupNavigation();

  // Start router (loads initial route from URL)
  this.router.start((path, params) => {
    // Default handler for unmatched routes
    console.warn(`No route matched for: ${path}, redirecting to dashboard`);
    this.router.navigate('', {}, true); // Replace history
  });
}
```

## Navigation Flow

### User Clicks "Edit" Button

```
1. Component emits:
   eventBus.emit('navigate', 'analysis-type-edit', { id: 'abc123' })

2. setupNavigation() handler:
   - Converts to router path: 'analysis-types/abc123/edit'
   - Calls: router.navigate('analysis-types/abc123/edit', { id: 'abc123' })

3. Router updates URL:
   - window.location.hash = '#/analysis-types/abc123/edit'

4. Router matches route:
   - Pattern: 'analysis-types/:id/edit'
   - Extracts: { id: 'abc123' }
   - Calls handler: (params) => this.navigate('analysis-type-edit', params)

5. App renders view:
   - this.navigate('analysis-type-edit', { id: 'abc123' })
   - Renders TypeFormView in edit mode
```

### User Clicks Browser Back Button

```
1. Browser triggers 'hashchange' event
   - Previous URL: #/analysis-types/abc123/edit
   - New URL: #/analysis-types

2. Router handleRoute():
   - Extracts path: 'analysis-types'
   - Matches route: 'analysis-types'
   - Calls handler: (params) => this.navigate('analysis-types-list', params)

3. App renders view:
   - Renders AnalysisTypesManager (list view)
```

### User Bookmarks and Visits URL

```
1. Browser loads: #/analysis-types/abc123/view

2. Router start() called in init():
   - Reads current hash: 'analysis-types/abc123/view'
   - Matches pattern: 'analysis-types/:id/view'
   - Extracts params: { id: 'abc123' }
   - Calls handler: this.navigate('analysis-type-view', { id: 'abc123' })

3. App renders view:
   - Loads analysis type 'abc123'
   - Renders TypeFormView in view mode
```

## Parameter Passing

### Route Parameters (in URL path)

Defined with `:paramName` in route pattern:

```javascript
// Route: analysis-types/:id/edit
// URL: #/analysis-types/abc123/edit
// Extracted: { id: 'abc123' }
```

### Query Parameters (not currently used, but supported)

Can be passed as additional parameters:

```javascript
router.navigate('analysis-types', { filter: 'recent', sort: 'name' });
// Results in: #/analysis-types?filter=recent&sort=name
```

## Best Practices

### 1. Always Use EventBus for Navigation

```javascript
// Good - Uses router automatically
this.eventBus.emit('navigate', 'analysis-type-edit', { id: typeId });

// Bad - Bypasses router, breaks URL sync
this.navigate('analysis-type-edit', { id: typeId });
```

### 2. Include Required Parameters

```javascript
// Good - ID included in params
this.eventBus.emit('navigate', 'analysis-type-view', { id: type.id });

// Bad - Missing required parameter
this.eventBus.emit('navigate', 'analysis-type-view');
```

### 3. Use Semantic Route Names

```javascript
// Good - Clear, semantic routes
#/analysis-types/abc123/edit
#/source-types/def456/view

// Bad - Cryptic or inconsistent
#/type?id=abc123&mode=edit
#/at/abc123/e
```

## Implementation Checklist

- [x] Router class created (`utils/Router.js`)
- [x] Routes defined in `setupRoutes()`
- [x] EventBus integrated with router
- [x] View-to-path mapping implemented
- [x] Router started in `init()`
- [x] Hash change listener active
- [x] Back/forward navigation working
- [x] Bookmarking supported
- [x] Deep linking enabled

## Testing

### Manual Testing Scenarios

1. **Bookmark a Type**
   - Navigate to #/analysis-types/abc123/view
   - Bookmark the page
   - Close browser
   - Open bookmark → Should load the same type in view mode

2. **Browser Navigation**
   - Navigate through several views
   - Click browser back button → Should return to previous view
   - Click browser forward button → Should go to next view
   - URL should update with each navigation

3. **Direct URL Access**
   - Type #/source-types/create in address bar
   - Press Enter
   - Should load the create source type form

4. **Shareable Links**
   - Copy URL while viewing a type: #/analysis-types/abc123/edit
   - Open in new tab/window
   - Should load the same type in edit mode

## Future Enhancements

### Potential Improvements

1. **Query Parameter Usage**
   - Use query params for filters, sorts, pagination
   - Example: `#/analysis-types?filter=recent&sort=name&page=2`

2. **Route Guards**
   - Validate user permissions before rendering views
   - Redirect unauthorized users to appropriate page

3. **Nested Routes**
   - Support deeper nesting for complex views
   - Example: `#/analyses/:id/documents/:docId`

4. **Route Transitions**
   - Add loading states during route changes
   - Implement route-based transitions/animations

5. **Server-Side Routing Migration**
   - Consider migrating from hash routing to HTML5 History API
   - Requires server-side configuration for SPA fallback

---

**End of Routing Documentation**
