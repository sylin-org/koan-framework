import { useParams, useNavigate, Link } from 'react-router-dom';
import { useState } from 'react';
import {
  useProject,
  useProjectStatus,
  useProjectHealth,
  useDeleteProject,
  useReindexProject,
  useIndexProject,
} from '@/hooks/useProjects';
import { useJobsByProject } from '@/hooks/useJobs';
import { useToast } from '@/hooks/useToast';
import ConfirmDialog from '@/components/ConfirmDialog';
import {
  FolderOpen,
  CheckCircle2,
  Loader2,
  XCircle,
  Clock,
  AlertCircle,
  ArrowLeft,
  Trash2,
  RefreshCw,
  Briefcase,
  HardDrive,
  Calendar,
  FileText,
  TrendingUp,
  AlertTriangle,
  Settings,
} from 'lucide-react';

export default function ProjectDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();

  // Fetch project data
  const { data: project, isLoading: projectLoading, error: projectError } = useProject(id!);
  const { data: status } = useProjectStatus(id!, 5000); // Poll every 5s
  const { data: health, isLoading: healthLoading } = useProjectHealth(id!);
  const { data: jobs, isLoading: jobsLoading } = useJobsByProject(id!, 10);

  // Mutations
  const deleteProject = useDeleteProject();
  const reindexProject = useReindexProject();
  const indexProject = useIndexProject();

  // UI state
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [showReindexDialog, setShowReindexDialog] = useState(false);
  const [showIndexDialog, setShowIndexDialog] = useState(false);

  const getStatusIcon = (projectStatus: string) => {
    switch (projectStatus) {
      case 'Ready':
        return <CheckCircle2 className="w-6 h-6 text-success-600" />;
      case 'Indexing':
        return <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />;
      case 'Failed':
        return <XCircle className="w-6 h-6 text-danger-600" />;
      default:
        return <Clock className="w-6 h-6 text-muted-foreground" />;
    }
  };

  const getStatusBadge = (projectStatus: string) => {
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
      <span className={`px-3 py-1 rounded-lg text-sm font-medium ${styles[projectStatus]}`}>
        {labels[projectStatus]}
      </span>
    );
  };

  const formatDate = (dateString: string | null | undefined) => {
    if (!dateString) return 'Never';
    const date = new Date(dateString);
    return date.toLocaleString();
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
  };

  const handleDelete = async () => {
    if (!project) return;
    try {
      await deleteProject.mutateAsync(project.id);
      toast.success('Project Deleted', `Successfully deleted project "${project.name}"`);
      navigate('/projects');
    } catch (error) {
      toast.error('Delete Failed', error instanceof Error ? error.message : 'Failed to delete project');
    }
  };

  const handleReindex = async () => {
    if (!project) return;
    try {
      await reindexProject.mutateAsync({ id: project.id, force: true });
      toast.success('Reindex Started', `Reindexing project "${project.name}"`);
      setShowReindexDialog(false);
    } catch (error) {
      toast.error('Reindex Failed', error instanceof Error ? error.message : 'Failed to start reindex');
    }
  };

  const handleIndex = async () => {
    if (!project) return;
    try {
      await indexProject.mutateAsync({ id: project.id, force: false });
      toast.success('Indexing Started', `Indexing project "${project.name}"`);
      setShowIndexDialog(false);
    } catch (error) {
      toast.error('Index Failed', error instanceof Error ? error.message : 'Failed to start indexing');
    }
  };

  const getJobStatusBadge = (jobStatus: string) => {
    const styles: Record<string, string> = {
      ['Pending']: 'bg-muted text-muted-foreground',
      ['Planning']: 'bg-yellow-100 text-yellow-800',
      ['Indexing']: 'bg-primary-100 text-primary-800',
      ['Completed']: 'bg-success-100 text-success-800',
      ['Failed']: 'bg-danger-100 text-danger-800',
      ['Cancelled']: 'bg-muted text-muted-foreground',
    };

    const labels: Record<string, string> = {
      ['Pending']: 'Pending',
      ['Planning']: 'Planning',
      ['Indexing']: 'Indexing',
      ['Completed']: 'Completed',
      ['Failed']: 'Failed',
      ['Cancelled']: 'Cancelled',
    };

    return (
      <span className={`px-2 py-1 rounded text-xs font-medium ${styles[jobStatus]}`}>
        {labels[jobStatus]}
      </span>
    );
  };

  if (projectError) {
    return (
      <div className="min-h-screen bg-background p-8">
        <div className="max-w-5xl mx-auto">
          <Link
            to="/projects"
            className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6 transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Back to Projects
          </Link>
          <div className="bg-danger-50 border border-danger-200 rounded-lg p-6">
            <div className="flex items-start gap-3">
              <AlertCircle className="w-6 h-6 text-danger-600 mt-0.5" />
              <div>
                <h3 className="text-lg font-semibold text-danger-900">Project not found</h3>
                <p className="text-danger-700 mt-1">
                  The project you're looking for doesn't exist or has been deleted.
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (projectLoading || !project) {
    return (
      <div className="min-h-screen bg-background p-8">
        <div className="max-w-5xl mx-auto">
          <div className="flex items-center justify-center py-12">
            <Loader2 className="w-8 h-8 text-primary-600 animate-spin" />
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-5xl mx-auto p-8">
        {/* Back Button */}
        <Link
          to="/projects"
          className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6 transition-colors"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Projects
        </Link>

        {/* Header */}
        <div className="flex items-start justify-between mb-8">
          <div className="flex items-start gap-4">
            <div className="p-3 bg-primary-100 rounded-lg">
              <FolderOpen className="w-8 h-8 text-primary-600" />
            </div>
            <div>
              <h1 className="text-3xl font-bold text-foreground">{project.name}</h1>
              <p className="text-muted-foreground mt-1 flex items-center gap-2">
                {project.rootPath}
              </p>
              <div className="flex items-center gap-3 mt-3">
                {getStatusIcon(project.status)}
                {getStatusBadge(project.status)}
              </div>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            {/* Index button for NotIndexed projects */}
            {project.status === 'NotIndexed' && (
              <button
                onClick={() => setShowIndexDialog(true)}
                disabled={indexProject.isPending}
                className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50"
              >
                <RefreshCw className="w-4 h-4" />
                Index Now
              </button>
            )}

            {/* Reindex button for Ready projects */}
            {project.status === 'Ready' && (
              <button
                onClick={() => setShowReindexDialog(true)}
                disabled={reindexProject.isPending}
                className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50"
              >
                <RefreshCw className="w-4 h-4" />
                Reindex
              </button>
            )}

            {/* Cancel & Restart button for stuck Indexing projects */}
            {project.status === 'Indexing' && (
              <button
                onClick={() => setShowReindexDialog(true)}
                disabled={reindexProject.isPending}
                className="inline-flex items-center gap-2 px-4 py-2 bg-warning-600 text-white rounded-lg hover:bg-warning-700 transition-colors disabled:opacity-50"
                title="Cancel current job and restart indexing"
              >
                <RefreshCw className="w-4 h-4" />
                Cancel & Restart
              </button>
            )}
            <Link
              to="/settings"
              className="inline-flex items-center gap-2 px-4 py-2 bg-muted text-foreground border border-border rounded-lg hover:bg-muted/80 transition-colors"
            >
              <Settings className="w-4 h-4" />
              Configure
            </Link>
            <button
              onClick={() => setShowDeleteDialog(true)}
              className="inline-flex items-center gap-2 px-4 py-2 bg-danger-600 text-white rounded-lg hover:bg-danger-700 transition-colors"
            >
              <Trash2 className="w-4 h-4" />
              Delete
            </button>
          </div>
        </div>

        {/* Error Banner */}
        {project.lastError && (
          <div className="bg-danger-50 border border-danger-200 rounded-lg p-4 mb-6">
            <div className="flex items-start gap-3">
              <AlertCircle className="w-5 h-5 text-danger-600 mt-0.5" />
              <div>
                <p className="text-sm font-semibold text-danger-900">Last Error</p>
                <p className="text-sm text-danger-700 mt-1">{project.lastError}</p>
              </div>
            </div>
          </div>
        )}

        {/* Metrics Grid */}
        <div className="grid grid-cols-4 gap-4 mb-8">
          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-2">
              <FileText className="w-4 h-4 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">Chunks</p>
            </div>
            <p className="text-2xl font-bold text-foreground">
              {project.documentCount.toLocaleString()}
            </p>
          </div>

          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-2">
              <HardDrive className="w-4 h-4 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">Indexed Size</p>
            </div>
            <p className="text-2xl font-bold text-foreground">
              {formatBytes(project.indexedBytes)}
            </p>
          </div>

          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-2">
              <Calendar className="w-4 h-4 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">Last Indexed</p>
            </div>
            <p className="text-sm font-medium text-foreground">
              {formatDate(project.lastIndexed)}
            </p>
          </div>

          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-2">
              <TrendingUp className="w-4 h-4 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">Status</p>
            </div>
            <p className="text-sm font-medium text-foreground">{status?.status || 'Unknown'}</p>
          </div>
        </div>

        {/* Health Status */}
        {!healthLoading && health && (
          <div className="bg-card border border-border rounded-lg p-6 mb-8">
            <h2 className="text-lg font-semibold text-foreground mb-4 flex items-center gap-2">
              <AlertTriangle className="w-5 h-5" />
              Health Status
            </h2>
            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-sm text-muted-foreground">Overall Health</span>
                <span className={`text-sm font-medium ${health.healthy ? 'text-success-600' : 'text-danger-600'}`}>
                  {health.healthy ? 'Healthy' : 'Unhealthy'}
                </span>
              </div>
              {health.warnings && health.warnings.length > 0 && (
                <div>
                  <p className="text-sm font-medium text-foreground mb-2">Warnings:</p>
                  <ul className="space-y-1">
                    {health.warnings.map((warning, index) => (
                      <li key={index} className="text-sm text-yellow-700 flex items-start gap-2">
                        <AlertTriangle className="w-4 h-4 mt-0.5" />
                        {warning}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          </div>
        )}

        {/* Active Job */}
        {status?.activeJob && (
          <div className="bg-card border border-primary-200 rounded-lg p-6 mb-8">
            <h2 className="text-lg font-semibold text-foreground mb-4 flex items-center gap-2">
              <Loader2 className="w-5 h-5 animate-spin text-primary-600" />
              Active Indexing Job
            </h2>
            <div className="space-y-4">
              <div className="flex items-center justify-between text-sm mb-2">
                <span className="text-muted-foreground">
                  {status.activeJob.processedFiles}/{status.activeJob.totalFiles} files
                </span>
                <span className="font-medium text-foreground">
                  {status.activeJob.progress.toFixed(1)}%
                </span>
              </div>
              <div className="w-full bg-muted rounded-full h-2">
                <div
                  className="bg-primary-600 h-2 rounded-full transition-all duration-300"
                  style={{ width: `${status.activeJob.progress}%` }}
                />
              </div>
              <div className="grid grid-cols-3 gap-4 text-sm">
                <div>
                  <p className="text-muted-foreground">Chunks Created</p>
                  <p className="font-medium text-foreground">
                    {status.activeJob.chunksCreated.toLocaleString()}
                  </p>
                </div>
                <div>
                  <p className="text-muted-foreground">Vectors Saved</p>
                  <p className="font-medium text-foreground">
                    {status.activeJob.vectorsSaved.toLocaleString()}
                  </p>
                </div>
                <div>
                  <p className="text-muted-foreground">Current Operation</p>
                  <p className="font-medium text-foreground">
                    {status.activeJob.currentOperation || 'Processing...'}
                  </p>
                </div>
              </div>
              <Link
                to={`/jobs/${status.activeJob.id}`}
                className="inline-flex items-center gap-2 text-sm text-primary-600 hover:text-primary-700 font-medium"
              >
                View Job Details →
              </Link>
            </div>
          </div>
        )}

        {/* Indexing History */}
        <div className="bg-card border border-border rounded-lg overflow-hidden">
          <div className="px-6 py-4 border-b border-border">
            <h2 className="text-lg font-semibold text-foreground flex items-center gap-2">
              <Briefcase className="w-5 h-5" />
              Indexing History
            </h2>
          </div>

          {jobsLoading ? (
            <div className="flex items-center justify-center py-12">
              <Loader2 className="w-8 h-8 text-primary-600 animate-spin" />
            </div>
          ) : jobs?.jobs && jobs.jobs.length > 0 ? (
            <div className="divide-y divide-border">
              {jobs.jobs.map((job: any) => (
                <Link
                  key={job.id}
                  to={`/jobs/${job.id}`}
                  className="block p-6 hover:bg-muted/30 transition-colors"
                >
                  <div className="flex items-start justify-between mb-2">
                    <div>
                      <div className="flex items-center gap-2 mb-1">
                        {getJobStatusBadge(job.status!)}
                        <span className="text-sm text-muted-foreground">
                          {formatDate(job.startedAt)}
                        </span>
                      </div>
                      {job.errorMessage && (
                        <p className="text-sm text-danger-600 mt-1">{job.errorMessage}</p>
                      )}
                    </div>
                    <div className="text-right text-sm">
                      <p className="text-muted-foreground">Files</p>
                      <p className="font-medium text-foreground">
                        {job.processedFiles}/{job.totalFiles}
                      </p>
                    </div>
                  </div>
                  <div className="grid grid-cols-4 gap-4 text-sm">
                    <div>
                      <p className="text-muted-foreground">Chunks</p>
                      <p className="font-medium text-foreground">
                        {job.chunksCreated?.toLocaleString() || 0}
                      </p>
                    </div>
                    <div>
                      <p className="text-muted-foreground">Vectors</p>
                      <p className="font-medium text-foreground">
                        {job.vectorsSaved?.toLocaleString() || 0}
                      </p>
                    </div>
                    <div>
                      <p className="text-muted-foreground">New Files</p>
                      <p className="font-medium text-success-600">{job.newFiles || 0}</p>
                    </div>
                    <div>
                      <p className="text-muted-foreground">Errors</p>
                      <p className="font-medium text-danger-600">{job.errorFiles || 0}</p>
                    </div>
                  </div>
                </Link>
              ))}
            </div>
          ) : (
            <div className="text-center py-12">
              <Briefcase className="w-12 h-12 text-muted-foreground mx-auto mb-3" />
              <p className="text-muted-foreground">No indexing jobs yet</p>
            </div>
          )}
        </div>

        {/* View All Jobs Link */}
        {jobs?.jobs && jobs.jobs.length > 0 && (
          <div className="mt-4 text-center">
            <Link
              to={`/jobs?project=${id}`}
              className="inline-flex items-center gap-2 text-sm text-primary-600 hover:text-primary-700 font-medium"
            >
              View All Jobs for This Project →
            </Link>
          </div>
        )}
      </div>

      {/* Delete Confirmation Dialog */}
      <ConfirmDialog
        isOpen={showDeleteDialog}
        onClose={() => setShowDeleteDialog(false)}
        onConfirm={handleDelete}
        title="Delete Project"
        message={`Are you sure you want to delete "${project.name}"? This will remove all indexed chunks and cannot be undone.`}
        confirmText="Delete Project"
        cancelText="Cancel"
        variant="danger"
        isLoading={deleteProject.isPending}
      />

      {/* Reindex Confirmation Dialog */}
      <ConfirmDialog
        isOpen={showReindexDialog}
        onClose={() => setShowReindexDialog(false)}
        onConfirm={handleReindex}
        title="Reindex Project"
        message={`This will force a complete reindex of "${project.name}". All existing chunks will be replaced. Continue?`}
        confirmText="Reindex"
        cancelText="Cancel"
        variant="warning"
        isLoading={reindexProject.isPending}
      />

      {/* Index Confirmation Dialog */}
      <ConfirmDialog
        isOpen={showIndexDialog}
        onClose={() => setShowIndexDialog(false)}
        onConfirm={handleIndex}
        title="Index Project"
        message={`This will start indexing "${project.name}". The system will scan all files and create searchable chunks. Continue?`}
        confirmText="Start Indexing"
        variant="info"
        isLoading={indexProject.isPending}
      />
    </div>
  );
}
