import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  BookOpen,
  Search,
  FolderPlus,
  PlayCircle,
  Settings,
  Zap,
  Info,
  AlertCircle,
  CheckCircle,
  Code,
  FileSearch,
  Database,
} from 'lucide-react';

export default function DocsPage() {
  const [activeSection, setActiveSection] = useState<string>('getting-started');

  const sections = [
    { id: 'getting-started', label: 'Getting Started', icon: PlayCircle },
    { id: 'search', label: 'Semantic Search', icon: Search },
    { id: 'projects', label: 'Managing Projects', icon: FolderPlus },
    { id: 'indexing', label: 'Indexing & Jobs', icon: FileSearch },
    { id: 'settings', label: 'Configuration', icon: Settings },
    { id: 'troubleshooting', label: 'Troubleshooting', icon: AlertCircle },
    { id: 'api', label: 'API Reference', icon: Code },
  ];

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-7xl mx-auto p-8">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-3xl font-bold text-foreground flex items-center gap-3">
            <BookOpen className="w-8 h-8" />
            Documentation
          </h1>
          <p className="text-muted-foreground mt-2">
            Learn how to use Koan.Context for semantic code search
          </p>
        </div>

        <div className="grid grid-cols-12 gap-6">
          {/* Sidebar Navigation */}
          <div className="col-span-3">
            <div className="bg-card border border-border rounded-lg p-2 sticky top-8">
              {sections.map((section) => {
                const Icon = section.icon;
                const isActive = activeSection === section.id;
                return (
                  <button
                    key={section.id}
                    onClick={() => setActiveSection(section.id)}
                    className={`w-full flex items-center gap-3 px-4 py-3 rounded-lg transition-colors text-left ${
                      isActive
                        ? 'bg-primary-100 text-primary-900'
                        : 'hover:bg-muted text-foreground'
                    }`}
                  >
                    <Icon className="w-5 h-5" />
                    <span className="font-medium">{section.label}</span>
                  </button>
                );
              })}
            </div>
          </div>

          {/* Content Area */}
          <div className="col-span-9">
            <div className="bg-card border border-border rounded-lg p-8">
              {activeSection === 'getting-started' && <GettingStarted />}
              {activeSection === 'search' && <SearchGuide />}
              {activeSection === 'projects' && <ProjectsGuide />}
              {activeSection === 'indexing' && <IndexingGuide />}
              {activeSection === 'settings' && <SettingsGuide />}
              {activeSection === 'troubleshooting' && <TroubleshootingGuide />}
              {activeSection === 'api' && <APIReference />}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function GettingStarted() {
  return (
    <div className="prose prose-sm max-w-none">
      <h2 className="text-2xl font-bold text-foreground mb-4">Getting Started</h2>

      <p className="text-muted-foreground mb-6">
        Koan.Context enables semantic search across your codebase using AI-powered embeddings.
        Get up and running in under 5 minutes.
      </p>

      <div className="bg-primary-50 border border-primary-200 rounded-lg p-4 mb-6">
        <div className="flex items-start gap-3">
          <Info className="w-5 h-5 text-primary-600 mt-0.5" />
          <div>
            <p className="text-sm font-medium text-primary-900">Quick Start</p>
            <p className="text-sm text-primary-700 mt-1">
              Follow these steps to index your first project and start searching.
            </p>
          </div>
        </div>
      </div>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Step 1: Create a Project</h3>
      <ol className="list-decimal list-inside space-y-2 text-foreground mb-6">
        <li>Navigate to <Link to="/projects" className="text-primary-600 hover:underline">Projects</Link></li>
        <li>Click "Create Project"</li>
        <li>Enter project name and root directory path</li>
        <li>Click "Create" to save</li>
      </ol>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Step 2: Index Your Code</h3>
      <ol className="list-decimal list-inside space-y-2 text-foreground mb-6">
        <li>Find your project in the list (status: "Not Indexed")</li>
        <li>Click the "Index" button</li>
        <li>Monitor progress on the <Link to="/jobs" className="text-primary-600 hover:underline">Jobs</Link> page</li>
        <li>Wait for completion (typically 1-5 minutes for small projects)</li>
      </ol>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Step 3: Search Your Code</h3>
      <ol className="list-decimal list-inside space-y-2 text-foreground mb-6">
        <li>Go to <Link to="/" className="text-primary-600 hover:underline">Search</Link></li>
        <li>Type a natural language query (e.g., "authentication middleware")</li>
        <li>Press Enter or click "Search"</li>
        <li>Browse results with code snippets and file locations</li>
      </ol>

      <div className="bg-success-50 border border-success-200 rounded-lg p-4 mt-6">
        <div className="flex items-start gap-3">
          <CheckCircle className="w-5 h-5 text-success-600 mt-0.5" />
          <div>
            <p className="text-sm font-medium text-success-900">You're Ready!</p>
            <p className="text-sm text-success-700 mt-1">
              Start exploring your codebase with semantic search. Try queries like "error handling",
              "database connection", or "API routes".
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}

function SearchGuide() {
  return (
    <div className="prose prose-sm max-w-none">
      <h2 className="text-2xl font-bold text-foreground mb-4">Semantic Search</h2>

      <p className="text-muted-foreground mb-6">
        Search your code using natural language instead of exact keywords. Powered by AI embeddings.
      </p>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">How It Works</h3>
      <p className="text-foreground mb-4">
        Koan.Context converts your code into vector embeddings that capture semantic meaning. When you search,
        your query is also embedded and compared to find the most relevant code chunks.
      </p>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Search Tips</h3>
      <ul className="list-disc list-inside space-y-2 text-foreground mb-6">
        <li><strong>Use natural language:</strong> "how to validate user input" instead of "validation"</li>
        <li><strong>Be specific:</strong> "JWT token generation" vs "authentication"</li>
        <li><strong>Describe intent:</strong> "connect to PostgreSQL database" finds DB code</li>
        <li><strong>Try variations:</strong> If results aren't great, rephrase your query</li>
      </ul>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Search Features</h3>
      <ul className="list-disc list-inside space-y-2 text-foreground mb-6">
  <li><strong>Project Filtering:</strong> Search within specific projects only</li>
  <li><strong>Tag Filters:</strong> Combine Any/All/Exclude tags to tune result scope</li>
  <li><strong>Tag Governance:</strong> Use the Tags view to inspect distribution and sample chunks</li>
        <li><strong>Pagination:</strong> Load more results with "Load More" button</li>
        <li><strong>Code Preview:</strong> See code snippets with syntax highlighting</li>
        <li><strong>Copy Code:</strong> One-click copy to clipboard</li>
        <li><strong>Open in Editor:</strong> Jump directly to file location (if configured)</li>
      </ul>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Example Queries</h3>
      <div className="bg-muted rounded-lg p-4 space-y-3">
        <div>
          <code className="text-sm font-mono text-foreground">authentication middleware</code>
          <p className="text-xs text-muted-foreground mt-1">Finds auth-related middleware code</p>
        </div>
        <div>
          <code className="text-sm font-mono text-foreground">database connection setup</code>
          <p className="text-xs text-muted-foreground mt-1">Locates DB initialization code</p>
        </div>
        <div>
          <code className="text-sm font-mono text-foreground">error handling patterns</code>
          <p className="text-xs text-muted-foreground mt-1">Discovers error handling approaches</p>
        </div>
        <div>
          <code className="text-sm font-mono text-foreground">REST API endpoints</code>
          <p className="text-xs text-muted-foreground mt-1">Finds API route definitions</p>
        </div>
      </div>
    </div>
  );
}

function ProjectsGuide() {
  return (
    <div className="prose prose-sm max-w-none">
      <h2 className="text-2xl font-bold text-foreground mb-4">Managing Projects</h2>

      <p className="text-muted-foreground mb-6">
        Projects represent codebases you want to index and search. Each project is indexed independently.
      </p>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Creating Projects</h3>
      <p className="text-foreground mb-4">
        Provide a name and root directory path. The system will discover all code files recursively.
      </p>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Project Statuses</h3>
      <ul className="list-disc list-inside space-y-2 text-foreground mb-6">
        <li><strong>Not Indexed:</strong> Project created but not yet indexed</li>
        <li><strong>Indexing:</strong> Currently processing files</li>
        <li><strong>Ready:</strong> Fully indexed and searchable</li>
        <li><strong>Failed:</strong> Indexing encountered errors</li>
      </ul>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Actions</h3>
      <ul className="list-disc list-inside space-y-2 text-foreground mb-6">
        <li><strong>Index:</strong> Start initial indexing (for Not Indexed projects)</li>
        <li><strong>Reindex:</strong> Re-scan and update index (for Ready projects)</li>
        <li><strong>Delete:</strong> Remove project and all indexed data</li>
        <li><strong>View Details:</strong> See metrics, health, and indexing history</li>
      </ul>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">File Monitoring</h3>
      <p className="text-foreground mb-4">
        When enabled, projects automatically reindex when files change. Monitor reindexing jobs
        on the Jobs page.
      </p>
    </div>
  );
}

function IndexingGuide() {
  return (
    <div className="prose prose-sm max-w-none">
      <h2 className="text-2xl font-bold text-foreground mb-4">Indexing & Jobs</h2>

      <p className="text-muted-foreground mb-6">
        Indexing converts your code into searchable vector embeddings. Jobs track indexing progress.
      </p>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Indexing Process</h3>
      <ol className="list-decimal list-inside space-y-2 text-foreground mb-6">
        <li><strong>Discovery:</strong> Find all code files in project directory</li>
        <li><strong>Chunking:</strong> Split files into semantic chunks (~1000 tokens)</li>
        <li><strong>Embedding:</strong> Generate vector embeddings for each chunk</li>
        <li><strong>Storage:</strong> Save vectors to Weaviate and metadata to SQL</li>
      </ol>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Job Statuses</h3>
      <ul className="list-disc list-inside space-y-2 text-foreground mb-6">
        <li><strong>Pending:</strong> Job created, waiting to start</li>
        <li><strong>Planning:</strong> Computing differential scan plan</li>
        <li><strong>Indexing:</strong> Actively processing files</li>
        <li><strong>Completed:</strong> Finished successfully</li>
        <li><strong>Failed:</strong> Encountered errors</li>
        <li><strong>Cancelled:</strong> Stopped by user</li>
      </ul>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Monitoring Jobs</h3>
      <p className="text-foreground mb-4">
        The Jobs page shows all indexing jobs with real-time progress updates (every 5 seconds for active jobs).
      </p>

      <ul className="list-disc list-inside space-y-2 text-foreground mb-6">
        <li><strong>Progress Bar:</strong> Visual completion percentage</li>
        <li><strong>Files Processed:</strong> Current/Total file count</li>
        <li><strong>Chunks Created:</strong> Number of code chunks indexed</li>
        <li><strong>Estimated Time:</strong> ETA for completion</li>
        <li><strong>Cancel:</strong> Stop active jobs if needed</li>
      </ul>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Performance</h3>
      <p className="text-foreground mb-4">
        Indexing speed depends on project size, file types, and system resources. Typical rates:
      </p>
      <ul className="list-disc list-inside space-y-2 text-foreground mb-6">
        <li>Small project (100 files): 1-2 minutes</li>
        <li>Medium project (1,000 files): 5-10 minutes</li>
        <li>Large project (10,000 files): 30-60 minutes</li>
      </ul>
    </div>
  );
}

function SettingsGuide() {
  return (
    <div className="prose prose-sm max-w-none">
      <h2 className="text-2xl font-bold text-foreground mb-4">Configuration</h2>

      <p className="text-muted-foreground mb-6">
        View and test application configuration. Settings are currently read-only and configured via appsettings.json.
      </p>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Settings Categories</h3>

      <div className="space-y-4">
        <div className="border border-border rounded-lg p-4">
          <h4 className="font-semibold text-foreground mb-2 flex items-center gap-2">
            <Database className="w-4 h-4" /> Vector Store
          </h4>
          <p className="text-sm text-muted-foreground">
            Weaviate connection settings, dimensions, distance metrics, and timeouts.
          </p>
        </div>

        <div className="border border-border rounded-lg p-4">
          <h4 className="font-semibold text-foreground mb-2 flex items-center gap-2">
            <Database className="w-4 h-4" /> Database
          </h4>
          <p className="text-sm text-muted-foreground">
            SQL database provider and connection string (SQLite by default).
          </p>
        </div>

        <div className="border border-border rounded-lg p-4">
          <h4 className="font-semibold text-foreground mb-2 flex items-center gap-2">
            <Zap className="w-4 h-4" /> AI Models
          </h4>
          <p className="text-sm text-muted-foreground">
            Embedding model provider, model name, and endpoint URL (Ollama by default).
          </p>
        </div>

        <div className="border border-border rounded-lg p-4">
          <h4 className="font-semibold text-foreground mb-2 flex items-center gap-2">
            <FileSearch className="w-4 h-4" /> Indexing
          </h4>
          <p className="text-sm text-muted-foreground">
            Chunk size, max file size, concurrent jobs, batch sizes, and parallelism settings.
          </p>
        </div>

        <div className="border border-border rounded-lg p-4">
          <h4 className="font-semibold text-foreground mb-2 flex items-center gap-2">
            <Settings className="w-4 h-4" /> Advanced
          </h4>
          <p className="text-sm text-muted-foreground">
            File monitoring, project resolution, job maintenance, and system settings.
          </p>
        </div>
      </div>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Connection Testing</h3>
      <p className="text-foreground mb-4">
        Use the "Test Connection" buttons to verify connectivity to Vector Store and Database.
        Helpful for troubleshooting configuration issues.
      </p>

      <div className="bg-primary-50 border border-primary-200 rounded-lg p-4 mt-6">
        <div className="flex items-start gap-3">
          <Info className="w-5 h-5 text-primary-600 mt-0.5" />
          <div>
            <p className="text-sm font-medium text-primary-900">Configuration File</p>
            <p className="text-sm text-primary-700 mt-1">
              To modify settings, edit <code className="px-2 py-1 bg-white rounded">appsettings.json</code> and restart the application.
              Runtime configuration updates are planned for a future release.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}

function TroubleshootingGuide() {
  return (
    <div className="prose prose-sm max-w-none">
      <h2 className="text-2xl font-bold text-foreground mb-4">Troubleshooting</h2>

      <p className="text-muted-foreground mb-6">
        Common issues and solutions for Koan.Context.
      </p>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Indexing Issues</h3>

      <div className="space-y-4">
        <div className="border-l-4 border-danger-600 bg-danger-50 p-4 rounded">
          <h4 className="font-semibold text-danger-900 mb-2">Job Status: Failed</h4>
          <p className="text-sm text-danger-800 mb-3">Indexing job completed with errors.</p>
          <p className="text-sm text-foreground font-medium mb-2">Solutions:</p>
          <ul className="list-disc list-inside text-sm text-foreground space-y-1">
            <li>Check job error message on Job Detail page</li>
            <li>Verify project path exists and is accessible</li>
            <li>Ensure Weaviate is running (check Settings → Test Vector Store)</li>
            <li>Check disk space and file permissions</li>
            <li>Try reindexing with the "Reindex" button</li>
          </ul>
        </div>

        <div className="border-l-4 border-yellow-600 bg-yellow-50 p-4 rounded">
          <h4 className="font-semibold text-yellow-900 mb-2">Indexing Very Slow</h4>
          <p className="text-sm text-yellow-800 mb-3">Job taking longer than expected.</p>
          <p className="text-sm text-foreground font-medium mb-2">Solutions:</p>
          <ul className="list-disc list-inside text-sm text-foreground space-y-1">
            <li>Large projects naturally take longer (10k+ files = 30-60 min)</li>
            <li>Check Settings → Indexing → Max Concurrent Jobs (increase if resources allow)</li>
            <li>Verify embedding model is responding (Ollama must be running)</li>
            <li>Check system resources (CPU, memory, disk I/O)</li>
          </ul>
        </div>
      </div>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Search Issues</h3>

      <div className="space-y-4">
        <div className="border-l-4 border-danger-600 bg-danger-50 p-4 rounded">
          <h4 className="font-semibold text-danger-900 mb-2">No Search Results</h4>
          <p className="text-sm text-danger-800 mb-3">Search returns 0 results.</p>
          <p className="text-sm text-foreground font-medium mb-2">Solutions:</p>
          <ul className="list-disc list-inside text-sm text-foreground space-y-1">
            <li>Ensure project is indexed (status: "Ready")</li>
            <li>Try a different search query (more specific or more general)</li>
            <li>Clear or relax tag filters (Any/All/Exclude) that may be eliminating matches</li>
            <li>Remove project filters to search all projects</li>
            <li>Check if project has 0 chunks (reindex may be needed)</li>
          </ul>
        </div>

        <div className="border-l-4 border-yellow-600 bg-yellow-50 p-4 rounded">
          <h4 className="font-semibold text-yellow-900 mb-2">Irrelevant Results</h4>
          <p className="text-sm text-yellow-800 mb-3">Search returns unexpected code.</p>
          <p className="text-sm text-foreground font-medium mb-2">Solutions:</p>
          <ul className="list-disc list-inside text-sm text-foreground space-y-1">
            <li>Rephrase query to be more specific</li>
            <li>Use more descriptive terms instead of generic keywords</li>
            <li>Require must-have tags in the "All" field or exclude noisy tags to focus results</li>
            <li>Filter to specific projects if searching wrong codebase</li>
          </ul>
        </div>
      </div>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Connection Issues</h3>

      <div className="space-y-4">
        <div className="border-l-4 border-danger-600 bg-danger-50 p-4 rounded">
          <h4 className="font-semibold text-danger-900 mb-2">Vector Store Connection Failed</h4>
          <p className="text-sm text-danger-800 mb-3">Cannot connect to Weaviate.</p>
          <p className="text-sm text-foreground font-medium mb-2">Solutions:</p>
          <ul className="list-disc list-inside text-sm text-foreground space-y-1">
            <li>Verify Weaviate container is running</li>
            <li>Check Settings → Vector Store → Host Port (default: 27501)</li>
            <li>Ensure no firewall blocking the port</li>
            <li>Restart Weaviate container</li>
            <li>Check Weaviate logs for errors</li>
          </ul>
        </div>
      </div>
    </div>
  );
}

function APIReference() {
  return (
    <div className="prose prose-sm max-w-none">
      <h2 className="text-2xl font-bold text-foreground mb-4">API Reference</h2>

      <p className="text-muted-foreground mb-6">
        REST API endpoints for programmatic access. Base URL: <code className="px-2 py-1 bg-muted rounded">http://localhost:27500</code>
      </p>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Projects</h3>
      <div className="space-y-4">
        <APIEndpoint method="GET" path="/api/projects" description="List all projects" />
        <APIEndpoint method="GET" path="/api/projects/{id}" description="Get project by ID" />
        <APIEndpoint method="POST" path="/api/projects" description="Create new project" />
        <APIEndpoint method="DELETE" path="/api/projects/{id}" description="Delete project" />
        <APIEndpoint method="POST" path="/api/projects/{id}/index" description="Start indexing" />
      </div>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Jobs</h3>
      <div className="space-y-4">
        <APIEndpoint method="GET" path="/api/jobs" description="List all jobs with pagination" />
        <APIEndpoint method="GET" path="/api/jobs/{id}" description="Get job by ID" />
        <APIEndpoint method="GET" path="/api/jobs/active" description="List active jobs" />
        <APIEndpoint method="POST" path="/api/jobs/{id}/cancel" description="Cancel job" />
      </div>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Search</h3>
      <div className="space-y-4">
        <APIEndpoint method="POST" path="/api/search" description="Semantic search" />
        <APIEndpoint method="POST" path="/api/search/suggestions" description="Get search suggestions" />
      </div>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Settings</h3>
      <div className="space-y-4">
        <APIEndpoint method="GET" path="/api/settings" description="Get application settings" />
        <APIEndpoint method="POST" path="/api/settings/test/vector-store" description="Test vector store connection" />
        <APIEndpoint method="POST" path="/api/settings/test/database" description="Test database connection" />
      </div>

      <h3 className="text-lg font-semibold text-foreground mt-6 mb-3">Example Request</h3>
      <div className="bg-muted rounded-lg p-4 font-mono text-sm">
        <div className="text-muted-foreground mb-2">POST /api/search</div>
        <pre className="text-foreground overflow-x-auto">{`{
  "query": "authentication middleware",
  "projectIds": ["project-guid"],
  "tagsAny": ["auth", "jwt"],
  "tagsExclude": ["sample"],
  "maxTokens": 5000
}`}</pre>
      </div>
    </div>
  );
}

function APIEndpoint({ method, path, description }: { method: string; path: string; description: string }) {
  const methodColors = {
    GET: 'bg-blue-100 text-blue-800',
    POST: 'bg-green-100 text-green-800',
    PUT: 'bg-yellow-100 text-yellow-800',
    DELETE: 'bg-red-100 text-red-800',
  };

  return (
    <div className="border border-border rounded-lg p-4">
      <div className="flex items-center gap-3 mb-2">
        <span className={`px-2 py-1 rounded text-xs font-bold ${methodColors[method as keyof typeof methodColors]}`}>
          {method}
        </span>
        <code className="text-sm font-mono text-foreground">{path}</code>
      </div>
      <p className="text-sm text-muted-foreground">{description}</p>
    </div>
  );
}
