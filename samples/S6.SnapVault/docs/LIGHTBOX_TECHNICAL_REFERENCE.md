# Lightbox Redesign - Technical Reference

**Purpose:** Technical details for implementation - current architecture, API endpoints, data structures

---

## Current Architecture (Before Redesign)

### File Locations

```
samples/S6.SnapVault/wwwroot/
├── js/
│   ├── app.js                    ← Main app, lightbox already registered
│   └── components/
│       └── lightbox.js           ← Current lightbox implementation (~500 lines)
├── css/
│   └── app.css                   ← Lightbox styles mixed in (lines ~800-1200)
└── index.html                    ← No changes needed
```

### Current `lightbox.js` Structure

```javascript
/**
 * Current implementation - Will be refactored
 */
export class Lightbox {
  constructor(app) {
    this.app = app;
    this.isOpen = false;
    this.currentPhotoId = null;
    this.currentIndex = 0;
    this.metadataPanelOpen = false;  // ← REMOVE: Consolidate into unified panel
    this.aiPanelOpen = false;        // ← REMOVE: Consolidate into unified panel
    this.render();
  }

  render() {
    // Creates modal DOM structure
  }

  open(photoId) {
    // Opens lightbox with photo
    // Fetches photo data from API: GET /api/photos/{id}
  }

  close() {
    // Closes lightbox, resets state
  }

  next() {
    // Navigate to next photo
  }

  previous() {
    // Navigate to previous photo
  }

  toggleMetadataPanel() {  // ← REMOVE
    // Shows EXIF metadata in right panel
  }

  toggleAIPanel() {        // ← REMOVE
    // Shows AI description in right panel
  }
}
```

### App Integration

```javascript
// In app.js (lines ~50-60, no changes needed)
import { Lightbox } from './components/lightbox.js';

// In App constructor:
this.components.lightbox = new Lightbox(this);

// Usage throughout app:
app.components.lightbox.open(photoId);
```

### Current Lightbox Features

**Existing Functionality:**
- ✅ Opens photo in modal view
- ✅ Left/right navigation arrows
- ✅ Close button (×)
- ✅ Displays photo at full size
- ✅ Two toggle buttons: Info / AI (separate panels)
- ✅ Panels slide in from right (desktop only)
- ❌ No zoom controls
- ❌ Limited keyboard (ESC only)
- ❌ No responsive panel (desktop only)

**UI Elements:**
- `.lightbox-overlay` - Dark backdrop (rgba(0,0,0,0.9))
- `.lightbox-content` - Photo container
- `.lightbox-nav` - Previous/next arrows
- `.lightbox-close` - Close button (×)
- `.lightbox-info-toggle` - Info button (ⓘ) ← REPLACE with unified toggle
- `.lightbox-ai-toggle` - AI button (🤖) ← REMOVE
- `.lightbox-metadata-panel` - Right slide panel ← REMOVE
- `.lightbox-ai-panel` - Right slide panel ← REMOVE

---

## API Endpoints

### Photo Endpoints (Already Implemented)

#### Get Photo by ID
```http
GET /api/photos/{id}
```

**Response:**
```json
{
  "id": "0199f067-a239-70c1-959f-73eca739a04c",
  "originalFileName": "DSC_1234.jpg",
  "eventId": "0199f067-1234-...",
  "width": 6000,
  "height": 4000,
  "size": 8600000,
  "contentType": "image/jpeg",
  "key": "photos/original/...",
  "galleryMediaKey": "gallery/...",
  "thumbnailMediaKey": "thumbnails/...",
  "masonryThumbnailMediaKey": "masonry-thumbnails/...",
  "cameraModel": "Canon EOS R5",
  "lensModel": "RF 85mm F1.2 L USM",
  "focalLength": "85mm",
  "aperture": "f/2.8",
  "shutterSpeed": "1/500",
  "iso": 400,
  "capturedAt": "2025-10-17T15:42:00Z",
  "location": {
    "latitude": 37.7749,
    "longitude": -122.4194,
    "altitude": 10
  },
  "detailedDescription": "A vibrant sunset over...",
  "autoTags": ["sunset", "bridge", "urban"],
  "detectedObjects": ["bridge", "sky", "water"],
  "moodDescription": "Serene, Awe-inspiring",
  "embedding": [...],
  "isFavorite": false,
  "rating": 0,
  "uploadedAt": "2025-10-17T15:45:00Z",
  "processingStatus": "Completed"
}
```

#### Toggle Favorite
```http
POST /api/photos/{id}/favorite
```

**Response:**
```json
{
  "isFavorite": true
}
```

#### Set Rating
```http
POST /api/photos/{id}/rate
Content-Type: application/json

{
  "rating": 4
}
```

**Response:**
```json
{
  "rating": 4
}
```

#### Download Photo
```http
GET /api/photos/{id}/download
```

**Response:** Redirects to `/storage/{key}` for full-resolution download

#### Delete Photo
```http
POST /api/photos/bulk/delete
Content-Type: application/json

{
  "photoIds": ["id1", "id2", ...]
}
```

**Response:**
```json
{
  "deleted": 2,
  "failed": 0,
  "errors": []
}
```

#### Regenerate AI Description
```http
POST /api/photos/{id}/regenerate-ai
```

**Response:**
```json
{
  "message": "AI regeneration started in background",
  "photoId": "0199f067-..."
}
```

**Note:** AI regeneration happens asynchronously. The AI description will be empty initially, then updated in the background.

---

## Photo Data Structure

### TypeScript Interface

```typescript
interface PhotoAsset {
  // Identity
  id: string;                    // GUID v7
  originalFileName: string;      // e.g., "DSC_1234.jpg"
  eventId: string;               // Parent event/album ID

  // Image properties
  width: number;                 // Original width (px)
  height: number;                // Original height (px)
  size: number;                  // File size (bytes)
  contentType: string;           // MIME type (image/jpeg)

  // Media references (storage keys)
  key: string;                   // Full-resolution photo
  galleryMediaKey: string;       // Gallery view (1920px)
  thumbnailMediaKey: string;     // Grid thumbnail (400px)
  masonryThumbnailMediaKey: string; // Masonry layout thumbnail

  // EXIF metadata (optional)
  cameraModel?: string;          // "Canon EOS R5"
  lensModel?: string;            // "RF 85mm F1.2 L USM"
  focalLength?: string;          // "85mm"
  aperture?: string;             // "f/2.8"
  shutterSpeed?: string;         // "1/500"
  iso?: number;                  // 400
  capturedAt?: string;           // ISO 8601 timestamp

  // Location (optional)
  location?: {
    latitude: number;
    longitude: number;
    altitude: number;
  };

  // AI-generated content (optional)
  detailedDescription?: string;  // AI vision description
  autoTags?: string[];           // AI-generated tags
  detectedObjects?: string[];    // Detected objects/scenes
  moodDescription?: string;      // Mood/atmosphere
  embedding?: number[];          // Vector for semantic search

  // User interactions
  isFavorite: boolean;
  rating: number;                // 0-5 stars

  // Timestamps
  uploadedAt: string;            // ISO 8601
  processingStatus: string;      // "Completed" | "Processing" | "Failed"
}
```

### JavaScript Access Pattern

```javascript
// Fetch photo data
const response = await fetch(`/api/photos/${photoId}`);
const photo = await response.json();

// Access properties
const {
  id,
  originalFileName,
  width,
  height,
  galleryMediaKey,         // Use for lightbox display
  cameraModel,
  aperture,
  shutterSpeed,
  iso,
  detailedDescription,     // AI description
  autoTags,                // AI tags
  isFavorite,
  rating
} = photo;

// Construct image URL
const imageUrl = `/storage/${photo.galleryMediaKey}`;

// Display EXIF metadata
const exif = {
  Camera: photo.cameraModel || 'Unknown',
  Lens: photo.lensModel || 'Unknown',
  Settings: `${photo.aperture || '?'} • ${photo.shutterSpeed || '?'} • ISO ${photo.iso || '?'}`,
  Focal: photo.focalLength || 'Unknown'
};

// Check if AI description exists
const hasAI = !!photo.detailedDescription;
```

---

## Target File Structure (After Redesign)

### New Files to Create

```
samples/S6.SnapVault/wwwroot/
├── js/components/
│   ├── lightbox.js               ← REFACTOR (remove dual panels)
│   ├── lightboxPanel.js          ← NEW (unified info panel)
│   ├── lightboxZoom.js           ← NEW (zoom system)
│   ├── lightboxActions.js        ← NEW (photo actions)
│   └── lightboxKeyboard.js       ← NEW (keyboard shortcuts)
└── css/
    ├── lightbox.css              ← NEW (extract from app.css)
    ├── lightbox-panel.css        ← NEW (panel styles)
    ├── lightbox-zoom.css         ← NEW (zoom UI)
    └── lightbox-responsive.css   ← NEW (responsive behavior)
```

### Import Structure

```javascript
// In lightbox.js
import { LightboxPanel } from './lightboxPanel.js';
import { LightboxZoom } from './lightboxZoom.js';
import { LightboxActions } from './lightboxActions.js';
import { LightboxKeyboard } from './lightboxKeyboard.js';

export class Lightbox {
  constructor(app) {
    this.app = app;
    this.panel = new LightboxPanel(this, app);
    this.zoom = new LightboxZoom(this);
    this.actions = new LightboxActions(this, app);
    this.keyboard = new LightboxKeyboard(this);
    // ...
  }
}
```

### CSS Import Strategy

**Option A: Import in app.css**
```css
/* At top of app.css */
@import 'lightbox.css';
@import 'lightbox-panel.css';
@import 'lightbox-zoom.css';
@import 'lightbox-responsive.css';
```

**Option B: Link in index.html** (Recommended for modularity)
```html
<!-- In <head> after app.css -->
<link rel="stylesheet" href="/css/lightbox.css">
<link rel="stylesheet" href="/css/lightbox-panel.css">
<link rel="stylesheet" href="/css/lightbox-zoom.css">
<link rel="stylesheet" href="/css/lightbox-responsive.css">
```

---

## Responsive Breakpoints

### CSS Variables (Design Tokens)

```css
:root {
  /* Breakpoints */
  --bp-mobile: 768px;
  --bp-tablet: 1200px;

  /* Panel dimensions */
  --panel-width-desktop: 380px;
  --panel-width-tablet: 320px;
  --panel-height-mobile: 70vh;

  /* Spacing */
  --spacing-xs: 8px;
  --spacing-sm: 12px;
  --spacing-md: 16px;
  --spacing-lg: 24px;
  --spacing-xl: 32px;

  /* Animation */
  --duration-fast: 150ms;
  --duration-normal: 300ms;
  --duration-slow: 500ms;
  --easing: cubic-bezier(0.4, 0, 0.2, 1);

  /* Colors */
  --bg-overlay: rgba(0, 0, 0, 0.9);
  --bg-panel: rgba(20, 20, 30, 0.95);
  --bg-panel-section: rgba(255, 255, 255, 0.05);
  --text-primary: rgba(255, 255, 255, 0.95);
  --text-secondary: rgba(255, 255, 255, 0.7);
  --text-tertiary: rgba(255, 255, 255, 0.5);
  --accent-primary: #3b82f6;
  --accent-hover: #60a5fa;
  --border-subtle: rgba(255, 255, 255, 0.1);

  /* Z-index stack */
  --z-lightbox-overlay: 1000;
  --z-lightbox-content: 1001;
  --z-lightbox-controls: 1002;
  --z-lightbox-panel: 1003;
  --z-lightbox-zoom-ui: 1004;
}
```

### Media Query Structure

```css
/* Mobile-first approach */

/* Base styles (mobile <768px) */
.info-panel {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  height: 70vh;
  transform: translateY(100%);
}

/* Tablet (768px - 1200px) */
@media (min-width: 768px) {
  .info-panel {
    width: 320px;
    height: 100vh;
    right: 0;
    left: auto;
    bottom: auto;
    transform: translateX(100%);
  }
}

/* Desktop (>1200px) */
@media (min-width: 1200px) {
  .info-panel {
    width: 380px;
    /* Panel pushes photo left instead of overlaying */
  }
}
```

---

## Performance Considerations

### GPU Acceleration

```css
/* Use transforms instead of position changes */
.info-panel {
  will-change: transform;
  transform: translateZ(0); /* Force GPU layer */
}

/* Avoid */
.info-panel.open {
  right: 0; /* ❌ Causes layout recalculation */
}

/* Prefer */
.info-panel.open {
  transform: translateX(0); /* ✅ GPU-accelerated */
}
```

### Image Loading Strategy

```javascript
// Use gallery image for lightbox (1920px max)
const imageUrl = `/storage/${photo.galleryMediaKey}`;

// Not full-resolution (could be 6000px+)
// const imageUrl = `/storage/${photo.key}`; // ❌ Too large
```

### Debouncing

```javascript
// Debounce resize events
let resizeTimeout;
window.addEventListener('resize', () => {
  clearTimeout(resizeTimeout);
  resizeTimeout = setTimeout(() => {
    this.handleResize();
  }, 150);
});

// Throttle zoom events
let zoomFrame;
element.addEventListener('wheel', (e) => {
  if (zoomFrame) return;
  zoomFrame = requestAnimationFrame(() => {
    this.handleZoom(e);
    zoomFrame = null;
  });
});
```

---

## Browser Compatibility

### Target Browsers
- Chrome 90+ (primary)
- Firefox 88+ (primary)
- Safari 14+ (primary)
- Edge 90+ (primary)

### Required Features
- CSS Grid (all modern browsers)
- CSS Custom Properties (all modern browsers)
- IntersectionObserver (all modern browsers)
- Touch Events (mobile/tablet)
- Pointer Events (progressive enhancement)

### Fallbacks
```javascript
// Touch events
if ('ontouchstart' in window) {
  // Enable pinch zoom, swipe gestures
} else {
  // Mouse-only interactions
}

// Reduced motion
const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
if (prefersReducedMotion) {
  // Disable animations or use instant transitions
}
```

---

## Next Steps

1. **Start Implementation:** See [Phase 1](./LIGHTBOX_PHASE_1.md) to begin
2. **Testing Reference:** See [Testing Guide](./LIGHTBOX_TESTING.md) for verification
3. **Overview:** See [Overview](./LIGHTBOX_OVERVIEW.md) for design vision

---

**Key Takeaways:**
- ✅ All API endpoints already exist - no backend changes needed
- ✅ Photo data structure is stable
- ✅ Current lightbox works - we're enhancing, not replacing
- ✅ Modular architecture allows incremental implementation
- ✅ Rollback strategy available at every phase
