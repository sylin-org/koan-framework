# KOAN-CONTEXT-001: Frontend Architecture Decision

**Status:** Accepted
**Date:** 2025-11-07
**Decider:** Architecture Review
**Related:** KOAN-CONTEXT-MASTER-IMPLEMENTATION-GUIDE.md

---

## Context

Koan.Context requires a modern, production-grade frontend to transform from MVP (D+ grade) to enterprise-ready (A grade). The implementation guide specifies React + TypeScript + Vite, but deployment architecture needed clarification.

---

## Decision

### Technology Stack

**Approved Stack:**
- **Framework:** React 18.3 + TypeScript 5.7
- **Build Tool:** Vite 6.0
- **Styling:** Tailwind CSS 3.4 (stable, not 4.0 beta)
- **Routing:** React Router 6 (BrowserRouter with deep linking)
- **State Management:**
  - TanStack Query 5 (server state)
  - Zustand 4 (UI state)
- **Component Library:** shadcn/ui (copy-paste, Tailwind-native)
- **Charts:** Recharts 2.14
- **Icons:** Lucide React
- **Testing:** Vitest + React Testing Library

**Rationale:** Matches KOAN-CONTEXT-MASTER-IMPLEMENTATION-GUIDE.md specification exactly.

---

### Deployment Architecture

**DECISION: Single-Server Architecture (No Separate Vite Dev Server)**

#### What We're Building

```
Single ASP.NET Core Process (port 27500)
├── Serves REST API (/api/*)
├── Serves MCP endpoints (/mcp/*)
└── Serves static React app from wwwroot/
```

#### Development Workflow

```
start.bat:
1. npm run build --watch (background) → outputs to wwwroot/ on file changes
2. dotnet watch run → serves from wwwroot/, restarts on C# changes
```

**Single URL:** `http://localhost:27500` for everything

#### Rejected Alternative

**Vite Dev Server (dual-process):**
```
- Vite dev server (port 5173) with HMR
- ASP.NET backend (port 27500)
- Requires CORS/proxy configuration
- Different behavior dev vs. prod
```

**Why Rejected:**
- Architectural complexity (two processes, two ports)
- Different behavior between dev and prod environments
- CORS/proxy configuration overhead
- Additional security surface area
- "Dangling service" anti-pattern

**Trade-off Accepted:**
- Lose instant HMR (~50ms updates)
- Accept 1-2 second rebuild time with Vite build watch
- Gain simplicity, production parity, single attack surface

---

### Build Output Requirements

#### 1. wwwroot/ Ownership

**RULE: wwwroot/ is 100% owned by Vite**

```
src/Koan.Context/wwwroot/
└── (Ephemeral - deleted and regenerated on every build)
    ├── index.html              # Vite output
    ├── assets/                 # Vite output (hashed bundles)
    │   ├── index-[hash].js
    │   ├── index-[hash].css
    │   └── [images/fonts]
    └── [static files from public/]
```

**Consequences:**
- ✅ Never manually edit files in wwwroot/
- ✅ All source lives in `Koan.Context.UI/src/`
- ✅ Static assets (favicon, robots.txt) go in `Koan.Context.UI/public/`
- ✅ Vite's `emptyOutDir: true` is safe (wwwroot/ is clean slate)

#### 2. Vite Configuration

```typescript
// vite.config.ts
export default defineConfig({
  build: {
    outDir: '../Koan.Context/wwwroot',  // NOT ../Koan.Context/wwwroot/dist
    emptyOutDir: true,
    sourcemap: true,
    minify: false  // Faster dev builds
  }
})
```

#### 3. ASP.NET Configuration

```csharp
// Program.cs
app.UseStaticFiles();              // Serves wwwroot/ root
app.MapFallbackToFile("index.html"); // SPA fallback (NO dist/ prefix)
```

```xml
<!-- Koan.Context.csproj -->
<ItemGroup>
  <!-- Exclude wwwroot from dotnet watch (UI changes don't restart backend) -->
  <Watch Remove="wwwroot\**\*" />
</ItemGroup>
```

---

### Routing & Deep Linking

**REQUIREMENT: Full deep linking support**

**Use Case:**
- User visits `/projects/abc-123` directly (bookmark, shared link)
- Must load project detail page immediately
- Browser back/forward must work naturally

**Implementation:**

```typescript
// App.tsx
<BrowserRouter>
  <Routes>
    <Route path="/" element={<Dashboard />} />
    <Route path="/projects" element={<ProjectsList />} />
    <Route path="/projects/:id" element={<ProjectDetail />} />
    <Route path="/projects/:id/settings" element={<ProjectSettings />} />
    <Route path="/search" element={<SearchPage />} />
    <Route path="/jobs" element={<JobsList />} />
    <Route path="/jobs/:id" element={<JobDetail />} />
    <Route path="/settings" element={<SettingsPage />} />
  </Routes>
</BrowserRouter>
```

**Backend Support:**
- `app.MapFallbackToFile("index.html")` handles all non-API routes
- All routes except `/api/*` and `/mcp/*` → index.html
- React Router takes over client-side routing

---

### start.bat Requirements

**REQUIREMENT: start.bat must build UI before running backend**

**Implementation:**

```batch
@echo off
echo Starting Koan.Context (Single Server Mode)...

echo [1/3] Installing dependencies...
cd src\Koan.Context.UI
if not exist "node_modules\" call npm ci

echo [2/3] Starting UI build watcher...
start /B "" npm run build -- --watch

echo [3/3] Starting ASP.NET server...
timeout /t 3
cd ..\Koan.Context
dotnet watch run

REM Cleanup on exit
taskkill /F /IM node.exe /T >nul 2>&1
```

**Guarantees:**
1. UI is built before backend starts
2. UI rebuilds automatically on file changes (1-2s)
3. Backend restarts automatically on C# changes
4. Single command to run everything
5. Clean shutdown kills background processes

---

## Consequences

### Positive

1. **Architectural Simplicity**
   - Single port, single URL
   - No CORS/proxy configuration
   - Matches traditional ASP.NET patterns

2. **Production Parity**
   - Development exactly matches production
   - No "works in dev, breaks in prod" surprises
   - Easier to debug deployment issues

3. **Security**
   - Single attack surface
   - All requests through ASP.NET middleware (rate limiting, auth, etc.)
   - No exposed dev server

4. **Operational Simplicity**
   - One process to monitor
   - One port to manage
   - Simpler Docker images

### Negative

1. **Slower Iteration**
   - 1-2 second rebuild vs. 50ms HMR
   - Manual browser refresh (F5) required
   - No state preservation during updates

2. **CSS Tweaking Workflow**
   - Change Tailwind class → Wait 1-2s → F5 → See result
   - Acceptable but noticeable vs. instant HMR

3. **Debugging**
   - Source maps work, but one extra step vs. dev server
   - No instant error overlays in browser

### Mitigation Strategies

1. **Fast Rebuilds**
   - Vite build watch is still fast (1-2s)
   - `minify: false` in dev for faster builds

2. **Browser Extensions**
   - Can use auto-reload extensions if manual F5 is too tedious

3. **Source Maps**
   - Enable in Vite config for debugging
   - TypeScript errors still visible in console/IDE

---

## Migration Path

### Phase 1: Scaffold (Current)
- Create `Koan.Context.UI/` project
- Install dependencies
- Configure Vite → wwwroot/
- Set up Tailwind with design tokens

### Phase 2: Core Infrastructure
- React Router setup
- API client with TypeScript types
- Layout components
- shadcn/ui base components

### Phase 3: Pages
- Dashboard (port from vanilla JS)
- Projects list & detail (with deep linking)
- Search page
- Jobs page

### Phase 4: Enhancement
- Storybook documentation
- Comprehensive testing
- Accessibility audit
- Performance optimization

---

## Compliance

**KOAN-CONTEXT-MASTER-IMPLEMENTATION-GUIDE.md Compliance:**

| Requirement | Status | Notes |
|-------------|--------|-------|
| React 18 + TypeScript | ✅ | Using 18.3 + 5.7 |
| Vite 5+ | ✅ | Using 6.0 |
| Tailwind CSS 3.4 | ✅ | Using 3.4.17 (stable) |
| React Router | ✅ | v6 with BrowserRouter |
| TanStack Query | ✅ | v5 for server state |
| Zustand | ✅ | v4 for UI state |
| Deep Linking | ✅ | Required, implemented |
| Storybook | 📋 | Planned (Phase 4) |
| Testing | 📋 | Planned (Vitest + RTL) |

**Deviations from Spec:**
- None (single-server approach is implementation detail, not spec violation)

---

## Review & Approval

**Approved By:** Architecture Review
**Date:** 2025-11-07
**Next Review:** After Phase 2 completion

---

## References

- [KOAN-CONTEXT-MASTER-IMPLEMENTATION-GUIDE.md](../proposals/KOAN-CONTEXT-MASTER-IMPLEMENTATION-GUIDE.md)
- [Vite Build Documentation](https://vitejs.dev/guide/build.html)
- [React Router BrowserRouter](https://reactrouter.com/en/main/router-components/browser-router)
