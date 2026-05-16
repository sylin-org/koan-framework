import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useProjects, useDeleteProject, useIndexProject, useReindexProject } from '@/hooks/useProjects';
import { useToast } from '@/hooks/useToast';
import { type Project } from '@/api/types';
import CreateProjectModal from '@/components/CreateProjectModal';
import ConfirmDialog from '@/components/ConfirmDialog';
import { ProjectCardSkeleton } from '@/components/Skeleton';
import {
  FolderOpen,
  CheckCircle2,
  Loader2,
  XCircle,
  Clock,
  AlertCircle,
  Plus,
  Trash2,
  RefreshCw,
  Play,
  Search,
  Filter
} from 'lucide-react';

export default function ProjectsList() {
  const { data: projects, isLoading, error } = useProjects();
  const deleteProject = useDeleteProject();
  const indexProject = useIndexProject();
  const reindexProject = useReindexProject();
  const toast = useToast();
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<string | 'all'>('all');
  const [sortBy, setSortBy] = useState<'name' | 'lastIndexed' | 'chunks'>('name');
  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>('asc');
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [projectToDelete, setProjectToDelete] = useState<Project | null>(null);

  // Check if there are indexing projects
  const hasIndexingProjects = projects?.some((p) => p.status === 'Indexing');

  // Get status icon and color
  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'Ready':
        return <CheckCircle2 className="w-5 h-5 text-success-600" />;
      case 'Indexing':
        return <Loader2 className="w-5 h-5 text-primary-600 animate-spin" />;
      case 'Failed':
        return <XCircle className="w-5 h-5 text-danger-600" />;
      default:
        return <Clock className="w-5 h-5 text-muted-foreground" />;
    }
  };

  const getStatusBadge = (status: string) => {
    const styles: Record<string, string> = {
      'NotIndexed': 'bg-muted text-muted-foreground',
      'Ready': 'bg-success-100 text-success-800',
      'Indexing': 'bg-primary-100 text-primary-800',
      'Failed': 'bg-danger-100 text-danger-800',
    };

    const labels: Record<string, string> = {
      'NotIndexed': 'Not Indexed',
      'Ready': 'Ready',
      'Indexing': 'Indexing',
      'Failed': 'Failed',
    };

    return (
      <span className={`px-2 py-1 rounded text-xs font-medium ${styles[status]}`}>
        {labels[status]}
      </span>
    );
  };

  const formatDate = (dateString: string | null | undefined) => {
    if (!dateString) return 'Never';
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;
    return date.toLocaleDateString();
  };

  // Filter and sort projects
  const filteredProjects = projects
    ?.filter((project) => {
      const matchesSearch =
        searchQuery === '' ||
        project.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
        project.rootPath.toLowerCase().includes(searchQuery.toLowerCase());

      const matchesStatus =
        statusFilter === 'all' || project.status === statusFilter;

      return matchesSearch && matchesStatus;
    })
    ?.sort((a, b) => {
      let comparison = 0;

      switch (sortBy) {
        case 'name':
          comparison = a.name.localeCompare(b.name);
          break;
        case 'lastIndexed':
          const aDate = a.lastIndexed ? new Date(a.lastIndexed).getTime() : 0;
          const bDate = b.lastIndexed ? new Date(b.lastIndexed).getTime() : 0;
          comparison = aDate - bDate;
          break;
        case 'chunks':
          comparison = a.documentCount - b.documentCount;
          break;
      }

      return sortOrder === 'asc' ? comparison : -comparison;
    });

  const toggleSort = (field: 'name' | 'lastIndexed' | 'chunks') => {
    if (sortBy === field) {
      setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc');
    } else {
      setSortBy(field);
      setSortOrder('asc');
    }
  };

  const handleDeleteConfirm = async () => {
    if (!projectToDelete) return;

    try {
      await deleteProject.mutateAsync(projectToDelete.id);
      toast.success('Project Deleted', `Successfully deleted project "${projectToDelete.name}"`);
      setProjectToDelete(null);
    } catch (error) {
      toast.error('Delete Failed', error instanceof Error ? error.message : 'Failed to delete project');
    }
  };

  const handleIndexProject = async (projectId: string) => {
    try {
      await indexProject.mutateAsync({ id: projectId, force: false });
      toast.success('Indexing Started', 'Project indexing has been initiated');
    } catch (error) {
      toast.error('Index Failed', error instanceof Error ? error.message : 'Failed to start indexing');
    }
  };

  const handleReindexProject = async (projectId: string) => {
    try {
      await reindexProject.mutateAsync({ id: projectId, force: true });
      toast.success('Reindex Started', 'Project reindexing has been initiated');
    } catch (error) {
      toast.error('Reindex Failed', error instanceof Error ? error.message : 'Failed to start reindex');
    }
  };

  if (error) {
    return (
      <div className="min-h-screen bg-background p-8">
        <div className="max-w-7xl mx-auto">
          <div className="bg-danger-50 border border-danger-200 rounded-lg p-6">
            <div className="flex items-start gap-3">
              <AlertCircle className="w-6 h-6 text-danger-600 mt-0.5" />
              <div>
                <h3 className="text-lg font-semibold text-danger-900">
                  Failed to load projects
                </h3>
                <p className="text-danger-700 mt-1">
                  {error instanceof Error ? error.message : 'Unknown error occurred'}
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-7xl mx-auto p-8">
        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-3xl font-bold text-foreground flex items-center gap-3">
              <FolderOpen className="w-8 h-8" />
              Projects
              {hasIndexingProjects && (
                <span className="flex items-center gap-2 text-sm font-normal text-primary-600 bg-primary-50 px-3 py-1 rounded-full">
                  <Loader2 className="w-3 h-3 animate-spin" />
                  Active indexing
                </span>
              )}
            </h1>
            <p className="text-muted-foreground mt-2">
              Manage and monitor your indexed code projects
            </p>
          </div>
          <button
            className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
            onClick={() => setIsCreateModalOpen(true)}
          >
            <Plus className="w-4 h-4" />
            Create Project
          </button>
        </div>

        {/* Stats Bar */}
        {projects && (
          <div className="grid grid-cols-4 gap-4 mb-6">
            <div className="bg-card border border-border rounded-lg p-4">
              <p className="text-sm text-muted-foreground">Total Projects</p>
              <p className="text-2xl font-bold text-foreground">{projects.length}</p>
            </div>
            <div className="bg-card border border-border rounded-lg p-4">
              <p className="text-sm text-muted-foreground">Ready</p>
              <p className="text-2xl font-bold text-success-600">
                {projects.filter((p) => p.status === 'Ready').length}
              </p>
            </div>
            <div className="bg-card border border-border rounded-lg p-4">
              <p className="text-sm text-muted-foreground">Indexing</p>
              <p className="text-2xl font-bold text-primary-600">
                {projects.filter((p) => p.status === 'Indexing').length}
              </p>
            </div>
            <div className="bg-card border border-border rounded-lg p-4">
              <p className="text-sm text-muted-foreground">Failed</p>
              <p className="text-2xl font-bold text-danger-600">
                {projects.filter((p) => p.status === 'Failed').length}
              </p>
            </div>
          </div>
        )}

        {/* Filters and Search */}
        <div className="bg-card border border-border rounded-lg p-4 mb-6">
          <div className="flex items-center gap-4">
            {/* Search */}
            <div className="flex-1 relative">
              <Search className="w-4 h-4 text-muted-foreground absolute left-3 top-1/2 -translate-y-1/2" />
              <input
                type="text"
                placeholder="Search by name or path..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full pl-10 pr-4 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
            </div>

            {/* Status Filter */}
            <div className="flex items-center gap-2">
              <Filter className="w-4 h-4 text-muted-foreground" />
              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value as string | 'all')}
                className="px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
              >
                <option value="all">All Statuses</option>
                <option value="NotIndexed">Not Indexed</option>
                <option value="Ready">Ready</option>
                <option value="Indexing">Indexing</option>
                <option value="Failed">Failed</option>
              </select>
            </div>
          </div>
        </div>

        {/* Projects Table */}
        <div className="bg-card border border-border rounded-lg overflow-hidden">
          {isLoading ? (
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead className="bg-muted/50 border-b border-border">
                  <tr>
                    <th className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Status
                    </th>
                    <th className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Project Name
                    </th>
                    <th className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Path
                    </th>
                    <th className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Chunks
                    </th>
                    <th className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Last Indexed
                    </th>
                    <th className="text-right px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Actions
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {[1, 2, 3, 4].map((i) => (
                    <ProjectCardSkeleton key={i} />
                  ))}
                </tbody>
              </table>
            </div>
          ) : filteredProjects && filteredProjects.length > 0 ? (
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead className="bg-muted/50 border-b border-border">
                  <tr>
                    <th className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Status
                    </th>
                    <th
                      className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider cursor-pointer hover:text-foreground"
                      onClick={() => toggleSort('name')}
                    >
                      <div className="flex items-center gap-2">
                        Project Name
                        {sortBy === 'name' && (
                          <span className="text-primary-600">
                            {sortOrder === 'asc' ? '↑' : '↓'}
                          </span>
                        )}
                      </div>
                    </th>
                    <th className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Path
                    </th>
                    <th
                      className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider cursor-pointer hover:text-foreground"
                      onClick={() => toggleSort('chunks')}
                    >
                      <div className="flex items-center gap-2">
                        Chunks
                        {sortBy === 'chunks' && (
                          <span className="text-primary-600">
                            {sortOrder === 'asc' ? '↑' : '↓'}
                          </span>
                        )}
                      </div>
                    </th>
                    <th
                      className="text-left px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider cursor-pointer hover:text-foreground"
                      onClick={() => toggleSort('lastIndexed')}
                    >
                      <div className="flex items-center gap-2">
                        Last Indexed
                        {sortBy === 'lastIndexed' && (
                          <span className="text-primary-600">
                            {sortOrder === 'asc' ? '↑' : '↓'}
                          </span>
                        )}
                      </div>
                    </th>
                    <th className="text-right px-6 py-3 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                      Actions
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {filteredProjects.map((project) => (
                    <tr
                      key={project.id}
                      className="hover:bg-muted/30 transition-colors"
                    >
                      <td className="px-6 py-4">
                        <div className="flex items-center gap-2">
                          {getStatusIcon(project.status)}
                          <div className="relative group">
                            {getStatusBadge(project.status)}
                            {/* Tooltip for additional health info */}
                            {project.lastError && project.status === 'Failed' && (
                              <div className="absolute left-0 top-full mt-2 w-64 p-3 bg-card border border-border rounded-lg shadow-lg opacity-0 invisible group-hover:opacity-100 group-hover:visible transition-all z-10">
                                <p className="text-xs font-semibold text-danger-600 mb-1">Error Details:</p>
                                <p className="text-xs text-foreground">{project.lastError}</p>
                              </div>
                            )}
                            {project.status === 'Ready' && project.documentCount > 0 && (
                              <div className="absolute left-0 top-full mt-2 w-48 p-3 bg-card border border-border rounded-lg shadow-lg opacity-0 invisible group-hover:opacity-100 group-hover:visible transition-all z-10">
                                <p className="text-xs text-muted-foreground">
                                  Last indexed: {formatDate(project.lastIndexed)}
                                </p>
                                <p className="text-xs text-muted-foreground mt-1">
                                  {project.documentCount.toLocaleString()} chunks indexed
                                </p>
                              </div>
                            )}
                            {project.status === 'Indexing' && (
                              <div className="absolute left-0 top-full mt-2 w-48 p-3 bg-card border border-border rounded-lg shadow-lg opacity-0 invisible group-hover:opacity-100 group-hover:visible transition-all z-10">
                                <p className="text-xs font-semibold text-primary-600 mb-1">Indexing in progress...</p>
                                <p className="text-xs text-muted-foreground">
                                  Check the Jobs page for details
                                </p>
                              </div>
                            )}
                          </div>
                        </div>
                      </td>
                      <td className="px-6 py-4">
                        <Link
                          to={`/projects/${project.id}`}
                          className="font-medium text-foreground hover:text-primary-600 transition-colors"
                        >
                          {project.name}
                        </Link>
                        {project.lastError && (
                          <p className="text-xs text-danger-600 mt-1 truncate max-w-md">
                            {project.lastError}
                          </p>
                        )}
                      </td>
                      <td className="px-6 py-4">
                        <p className="text-sm text-muted-foreground truncate max-w-md">
                          {project.rootPath}
                        </p>
                      </td>
                      <td className="px-6 py-4">
                        <p className="text-sm font-medium text-foreground">
                          {project.documentCount.toLocaleString()}
                        </p>
                      </td>
                      <td className="px-6 py-4">
                        <p className="text-sm text-muted-foreground">
                          {formatDate(project.lastIndexed)}
                        </p>
                      </td>
                      <td className="px-6 py-4">
                        <div className="flex items-center justify-end gap-2">
                          {/* Index button for NotIndexed projects */}
                          {project.status === 'NotIndexed' && (
                            <button
                              className="inline-flex items-center gap-1 px-3 py-1 text-sm text-primary-600 hover:bg-primary-50 rounded transition-colors disabled:opacity-50"
                              onClick={() => handleIndexProject(project.id)}
                              disabled={indexProject.isPending}
                            >
                              <Play className="w-3 h-3" />
                              Index
                            </button>
                          )}

                          {/* Reindex button for Ready projects */}
                          {project.status === 'Ready' && (
                            <button
                              className="inline-flex items-center gap-1 px-3 py-1 text-sm text-primary-600 hover:bg-primary-50 rounded transition-colors disabled:opacity-50"
                              onClick={() => handleReindexProject(project.id)}
                              disabled={reindexProject.isPending}
                            >
                              <RefreshCw className="w-3 h-3" />
                              Reindex
                            </button>
                          )}

                          {/* Cancel & Restart button for stuck Indexing projects */}
                          {project.status === 'Indexing' && (
                            <button
                              className="inline-flex items-center gap-1 px-3 py-1 text-sm text-warning-600 hover:bg-warning-50 rounded transition-colors disabled:opacity-50"
                              onClick={() => handleReindexProject(project.id)}
                              disabled={reindexProject.isPending}
                              title="Cancel current job and restart indexing"
                            >
                              <RefreshCw className="w-3 h-3" />
                              Cancel & Restart
                            </button>
                          )}

                          {/* Delete button */}
                          <button
                            className="inline-flex items-center gap-1 px-3 py-1 text-sm text-danger-600 hover:bg-danger-50 rounded transition-colors"
                            onClick={() => setProjectToDelete(project)}
                          >
                            <Trash2 className="w-3 h-3" />
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="text-center py-12">
              <FolderOpen className="w-12 h-12 text-muted-foreground mx-auto mb-3" />
              <p className="text-muted-foreground mb-4">
                {searchQuery || statusFilter !== 'all'
                  ? 'No projects match your filters'
                  : 'No projects yet'}
              </p>
              {!searchQuery && statusFilter === 'all' && (
                <button
                  className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
                  onClick={() => setIsCreateModalOpen(true)}
                >
                  <Plus className="w-4 h-4" />
                  Create Your First Project
                </button>
              )}
            </div>
          )}
        </div>

        {/* Footer Stats */}
        {filteredProjects && filteredProjects.length > 0 && (
          <div className="mt-4 text-sm text-muted-foreground text-center">
            Showing {filteredProjects.length} of {projects?.length || 0} projects
          </div>
        )}
      </div>

      {/* Create Project Modal */}
      <CreateProjectModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
      />

      {/* Delete Confirmation Dialog */}
      <ConfirmDialog
        isOpen={!!projectToDelete}
        onClose={() => setProjectToDelete(null)}
        onConfirm={handleDeleteConfirm}
        title="Delete Project"
        message={`Are you sure you want to delete "${projectToDelete?.name}"? This will remove all indexed chunks and cannot be undone.`}
        confirmText="Delete Project"
        cancelText="Cancel"
        variant="danger"
        isLoading={deleteProject.isPending}
      />
    </div>
  );
}
