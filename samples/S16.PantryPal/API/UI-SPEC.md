# S16 PantryPal — UI Specification (AngularJS + Tailwind)

Calm Utility UI for a single-page AngularJS app using `ngRoute` with hash-based routing. Mobile-first with responsive density. Entity-first REST surface is primary (api/data/_ with q, filter, sort, page, pageSize, with, view); actions are verb routes under api/action/_.

## Routes

- #/dashboard
- #/pantry
- #/capture
- #/review
- #/confirm/:photoId
- #/meals
- #/shopping-list
- #/insights
- #/behind-the-scenes

## Global Shell & Patterns

- Navigation
  - Mobile: bottom nav (Dashboard, Pantry, Capture, Meals, Insights, Shopping); persistent FAB [Add via Camera] on primary views
  - Desktop: top nav with breadcrumb; optional left rail on ≥1280px for Pantry filters
- Visual language
  - Variant A — Calm Utility (default): neutral bg (bg-gray-50), white cards, rounded-2xl, shadow-sm, primary accents sparingly
- Smart paging
  - Default infinite scroll on mobile; switch to pager on desktop or when any filter active
  - pageSize=50 across the app; omit pageSize selector to keep UI calm
  - Persist pager state where applicable; server clamp remains authoritative
- Search degraded chip
  - If `X-Search-Degraded: 1` on api/data responses: show chip near search input “Semantic offline — using lexical” with tooltip
  - [Try again] repeats the last query
- Optimistic updates
  - Pantry qty/unit steppers update immediately and send `PATCH /api/data/pantry/{id}` with partial fields; revert and toast on error
- Toasts
  - Top-right container; success/info/warn/error; do not block interactions
- Test Auth (browse-only)
  - When unauthenticated: disable write CTAs and show subtle banner “Browse-only (Test Auth) — Sign in to edit”
  - Mirror S5.Recs pattern for detection

## Data Endpoints & Params

- Data: `GET /api/data/{model}?filter={...}&q=&page=&pageSize=&sort=&with=&view=`
  - pageSize default 50
  - Use q for hybrid search (semantic+lexical when available)
  - with is supported; with=\* allowed (use judiciously)
- Actions:
  - `POST /api/action/pantry/upload` (multipart)
  - `POST /api/action/pantry/confirm/{photoId}`
  - `GET  /api/pantry-insights/stats`

## #/dashboard

Frame

- Header bar: app name, global search with JSON filter toggle (modal)
- Quick Actions: FAB [Add via Camera]; overflow includes [Upload]
- Sections (stack)
  1. Expiring Soon (card list, max 5)
  2. Suggested Meals (carousel of 3)
  3. Insights Mini (sparkline + totals)

Bindings

- Expiring Soon: `GET /api/data/pantry?filter={"ExpiresAt":{"$lte":"<ISO+7days>"},"Status":"available"}&page=1&pageSize=5&sort=ExpiresAt`
- Suggested Meals: `POST /api/meals/suggest` (mock allowed)
- Insights mini: `GET /api/pantry-insights/stats`
- Degraded chip if any above returns `X-Search-Degraded: 1`

States

- Loading skeletons; Empty and Error banners with retry

A11y

- FAB aria-label; carousel operable via keyboard; chips described via aria-labels

## #/pantry (Inventory)

Controls

- Search (q=) input; JSON filter editor (modal)
- Filter chips: Category, Status, “Expiring <7d” (writes filter JSON)
- Sort dropdown: ExpiresAt↑↓, Name↑↓, Category↑↓ (writes sort)

Layout

- Mobile: 2-col card grid, infinite scroll
- Desktop: 3–4 cols; show pager when desktop or when any filter active

Card

- Title (name), category chip, status chip
- Qty stepper (optimistic) and unit; `PATCH /api/data/pantry/{id}` partial
- Expires chip (color by threshold); optional photo thumb
- [Edit] opens side drawer with full details; [More] includes delete (confirm)

Banners

- Degraded chip near search if header present

States

- Loading skeleton grid; Empty prompt to Capture/Upload; Error banner

A11y

- Stepper controls keyboard operable; drawer focus trapping; visible focus rings

## #/capture → #/review → #/confirm/:photoId

#/capture

- CTAs: [Take Photo] and [Upload Photo]; camera permission handling; recent photos tray
- On success (upload): navigate to `#/review?photoId=...`

#/review

- Photo canvas with bounding boxes; select opens right drawer
- Drawer
  - Top candidates (3 chips with confidences)
  - [Choose different…] opens NL modal; live parse preview
  - Edge badge “Already added” for confirmed items
- Build confirmations[] in local state

#/confirm/:photoId

- POST `/api/action/pantry/confirm/{photoId}` with confirmations[]
- Success summary: items created/updated, duplicates suppressed, shelf-life notes
- Actions: [Add another] and [Go to Pantry]

A11y

- Boxes focusable with visible outlines; drawer and modal keyboard navigable; Escape closes

## #/meals (Suggest & Plan)

Controls

- DietaryRestrictions multiselect; maxCookingMinutes slider; CTA [Suggest meals]

Results

- Recipe cards with score, availability; missingIngredients chips; CTA [Add to Plan]

Planner

- Days columns with droppable slots; total time indicator; CTA [Generate Shopping List]

APIs

- POST `/api/meals/suggest`
- POST `/api/meals/plan`
- POST `/api/meals/shopping/{planId}`

States

- Loading, Empty, Partial success table for plan/shopping

A11y

- Drag-drop keyboard equivalent via [Move] menu; ARIA live announcements

## #/shopping-list

- Group by aisle/category; badges: “from plan” / “pantry refill”
- Checkable items; bulk actions per group; export [Copy] / [Export CSV]
- Empty: “Nothing missing — great job!”

A11y

- Group headings; checkbox labels; keyboard access for bulk and export

## #/insights

- Cards: waste trend (sparkline), soon-to-expire breakdown (bars), cuisine coverage (donut)
- Linkouts to Pantry with pre-filled filters
- Degraded/empty visuals; toggle “Show data table” for each chart

A11y

- Charts have aria-label and data table toggles; keyboard navigable segments

## #/behind-the-scenes (Koan)

Sections

1. Entity-first patterns: curl snippets for current route params (CRUD, paging, filter JSON, sort, q, with)
2. Capability matrix: list features visible (e.g., “EntityController<T> • paging, filter, sort, q, with”)
3. Requests: last 3 API calls (redacted), include headers like `X-Search-Degraded`
4. Environment: test auth status, paging defaults

Bindings

- `$http` interceptor feeds a ring buffer; copy-to-clipboard on snippets

A11y

- Code blocks selectable; copy buttons have aria-label and success announcements

## Koan Capabilities Overlay

- Toggle in user menu (desktop) or footer (mobile)
- Pills on major UI elements show: entity/action, params, paging mode
- Tooltip on focus: endpoints used, current params, server vs infinite paging
- Non-blocking overlay; Escape disables

## Fixtures (realism)

- PantryItem: baby spinach (bag, produce, expires 2025-10-12), whole milk (0.5 gal, dairy, 2025-10-10), chicken breast (2 lbs, meat, 2025-10-09)
- Recipes: Penne Arrabbiata (30m, availability 0.85, missing: red chili flakes), Spinach Omelette (12m, availability 0.92, missing: [])

## Implementation Notes

- Route constants should be centralized to avoid magic strings
- Use MVC controllers for actions (no inline endpoints)
- Prefer minimal `with` on listings; expose `with=*` only in developer/debug flows
- Keep PATCH payloads partial and small for optimistic updates
