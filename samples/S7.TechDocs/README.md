# S7.TechDocs - Documentation Platform Demo

A fully functional mock-up of the S7 TechDocs platform, implementing the mode-based architecture with role-aware UI and AI assistance features.

## Features Implemented

### 🎯 Mode-Based Architecture
- **Browse Mode**: Content discovery with search, filters, and AI suggestions
- **View Mode**: Enhanced article reading experience with rich features
- **Edit Mode**: Document creation/editing with live preview and AI assistance  
- **Moderation Mode**: Review workflow with kanban-style pipeline
- **Admin Mode**: User management and system configuration

### 📖 Enhanced View KB Article Mode
The View mode provides a comprehensive article reading experience:

#### Navigation & Orientation
- **Breadcrumbs**: Clear navigation path (Browse → Collection → Article)
- **Back to Browse**: Quick return with state preservation
- **Collection filtering**: Click breadcrumb items to filter content

#### Rich Article Display
- **Professional layout**: Two-column design with sidebar
- **Article metadata**: Author, collection, view count, ratings
- **Reading time**: Auto-calculated based on word count
- **Interactive tags**: Click to search for related content
- **Status indicators**: Visual badges for content lifecycle

#### Intelligent Sidebar
- **Table of Contents**: Auto-generated from headings with smooth scrolling
- **Article Statistics**: Views, ratings, dates with visual indicators
- **Author Information**: Profile with article count and role
- **Sticky positioning**: Follows scroll for easy navigation

#### Interactive Actions
- **Bookmark system**: Save articles with localStorage persistence
- **Share functionality**: Native Web Share API with clipboard fallback
- **Article rating**: Thumbs up/down feedback system
- **Issue reporting**: Modal form for content problems
- **Related articles**: Smart suggestions based on collection and tags

#### Advanced Features
- **Smooth scrolling**: Enhanced navigation between sections
- **Heading highlighting**: Visual feedback when scrolling to TOC items
- **Role-based actions**: Edit button appears only for authorized users
- **Responsive design**: Adapts beautifully to mobile and desktop

### 🔐 Role-Based Access Control
- **Reader**: Can browse and view published documents
- **Author**: Can create, edit, and submit documents for review
- **Moderator**: Can review, approve, and publish documents
- **Admin**: Full system access including user management

### 🤖 AI Features (Mock Implementation)
- Content quality scoring
- Tag suggestions
- Table of contents generation
- Writing improvement suggestions
- Related document recommendations

### 🎨 Visual Design
- Borrows S5.Recs dark theme and card-based layouts
- Purple gradient branding with S7 focus
- Responsive design with mobile support
- Smooth transitions between modes

## Quick Start

1. **Run the application:**
   ```bash
   cd samples/S7.TechDocs
   dotnet run
   ```

2. **Open browser:**
   ```
   https://localhost:5001
   ```

3. **Try different roles:**
   Use the "Demo Role" dropdown in the top-right to switch between:
   - Reader (browse only)
   - Author (browse + edit)
   - Moderator (browse + edit + moderate)
   - Admin (all features)

## Demo Features

### Browse Mode
- **Hero Search**: Large search interface with AI search button
- **Smart Filters**: Filter by collection, status, and other criteria
- **Document Cards**: Rich previews with ratings, tags, and metadata
- **Sorting Options**: Recently updated, highest rated, most viewed, alphabetical

### Edit Mode  
- **Split Editor**: Markdown editor with live preview
- **AI Assistant Panel**: Quality scoring, tag suggestions, improvement tips
- **Document Metadata**: Title, summary, collection, tags
- **Auto-save**: Preserves work as you type

### Moderation Mode
- **Workflow Kanban**: Visual pipeline (Submitted → Review → Approved → Returned)
- **Bulk Actions**: Multi-select approve, comment, notify
- **Review Notes**: Add feedback for authors
- **Status Updates**: Quick approve/reject actions

### Admin Mode
- **User Management**: View all users with roles and activity
- **Role Configuration**: Manage permission groups
- **System Settings**: AI provider, search engine status
- **Health Monitoring**: Connection status and system health

## Mock Data

The demo includes realistic sample data:
- **5 Documents**: Covering getting started, API auth, data patterns, deployment, FAQ
- **5 Collections**: Getting Started, Guides, API Reference, FAQ, Troubleshooting  
- **5 Users**: One for each role level with activity history
- **Search Results**: Semantic search simulation with relevance scoring

## Technical Implementation

### Backend (ASP.NET Core)
- **Controllers**: RESTful APIs for documents, collections, users, search, AI
- **Services**: Mock data services with realistic business logic
- **Auth**: TestProvider simulation with role-based policies
- **Models**: Complete domain models matching proposal specifications

### Frontend (Vanilla JS + Tailwind)
- **Modular Architecture**: Separate files for API, auth, UI utilities, main app
- **Real Routing**: Client-side mode management with URL persistence
- **Responsive Design**: Mobile-first with progressive enhancement
- **Accessibility**: Keyboard navigation and screen reader support

### Key Files
```
├── Controllers/          # API endpoints
├── Services/            # Business logic with mock data
├── Models/              # Domain models
├── Infrastructure/      # Constants, middleware, utilities
└── wwwroot/
    ├── index.html       # Single-page application shell
    ├── styles.css       # S5-inspired styling
    └── js/
        ├── app.js       # Main application logic
        ├── api.js       # API client
        ├── auth.js      # Authentication & role management
        └── ui.js        # UI utilities & components
```

## Role Simulation

The demo uses query parameters to simulate different authentication states:
- `?role=Reader` - Basic read-only access
- `?role=Author` - Can create and edit documents  
- `?role=Moderator` - Can review and approve content
- `?role=Admin` - Full administrative access

Role switching preserves state and updates the UI to show/hide features based on permissions.

## UX Testing Points

1. **Mode Transitions**: Test switching between modes and data persistence
2. **Role Permissions**: Verify features show/hide correctly for each role
3. **Search Experience**: Try various search terms and filter combinations
4. **Edit Workflow**: Create, edit, save documents and test AI assistance
5. **Moderation Flow**: Test document approval workflow and status changes
6. **Responsive Design**: Test on mobile devices and different screen sizes

## Next Steps

This mock-up provides a complete foundation for:
- UX feedback and iteration
- Technical validation of the mode-based architecture  
- Role-based permission model testing
- AI integration planning
- Visual design refinement

Ready for user testing and stakeholder feedback! 🚀
