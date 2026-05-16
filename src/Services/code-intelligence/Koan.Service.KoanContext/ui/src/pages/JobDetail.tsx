import { useParams, Link } from 'react-router-dom';
import { useState } from 'react';
import { useJob, useCancelJob } from '@/hooks/useJobs';
import { useProject } from '@/hooks/useProjects';
import { useToast } from '@/hooks/useToast';
import ConfirmDialog from '@/components/ConfirmDialog';
import { JobDetailSkeleton } from '@/components/Skeleton';
import {
  Briefcase,
  CheckCircle2,
  Loader2,
  XCircle,
  Clock,
  AlertCircle,
  ArrowLeft,
  Ban,
  FileText,
  TrendingUp,
  Calendar,
  Zap,
  HardDrive,
  AlertTriangle,
  CheckCheck,
  XOctagon,
  MinusCircle,
  FolderOpen,
} from 'lucide-react';

export default function JobDetail() {
  const { id } = useParams<{ id: string }>();
  const toast = useToast();

  // Determine if job is active to enable polling
  const { data: jobPreview } = useJob(id!, undefined);
  const isActive =
    jobPreview?.status === 'Pending' ||
    jobPreview?.status === 'Planning' ||
    jobPreview?.status === 'Indexing';

  // Fetch job with polling for active jobs
  const {
    data: job,
    isLoading: jobLoading,
    error: jobError,
  } = useJob(id!, isActive ? 5000 : undefined);

  // Fetch project info
  const { data: project } = useProject(job?.projectId || '');

  // Mutations
  const cancelJob = useCancelJob();

  // UI state
  const [showCancelDialog, setShowCancelDialog] = useState(false);

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'Completed':
        return <CheckCircle2 className="w-6 h-6 text-success-600" />;
      case 'Failed':
        return <XCircle className="w-6 h-6 text-danger-600" />;
      case 'Cancelled':
        return <Ban className="w-6 h-6 text-muted-foreground" />;
      case 'Indexing':
        return <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />;
      case 'Planning':
        return <Clock className="w-6 h-6 text-yellow-600" />;
      default:
        return <Clock className="w-6 h-6 text-muted-foreground" />;
    }
  };

  const getStatusBadge = (status: string) => {
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
      <span className={`px-3 py-1 rounded-lg text-sm font-medium ${styles[status]}`}>
        {labels[status]}
      </span>
    );
  };

  const formatDuration = (isoString: string) => {
    const match = isoString.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?/);
    if (!match) return isoString;

    const hours = parseInt(match[1] || '0');
    const minutes = parseInt(match[2] || '0');
    const seconds = parseInt(match[3] || '0');

    if (hours > 0) return `${hours}h ${minutes}m ${seconds}s`;
    if (minutes > 0) return `${minutes}m ${seconds}s`;
    return `${seconds}s`;
  };

  const formatDate = (dateString: string | null | undefined) => {
    if (!dateString) return 'N/A';
    const date = new Date(dateString);
    return date.toLocaleString();
  };

  const handleCancel = async () => {
    if (!job) return;
    try {
      await cancelJob.mutateAsync(job.id);
      toast.success('Job Cancelled', 'Indexing job has been cancelled successfully');
      setShowCancelDialog(false);
    } catch (error) {
      toast.error('Cancel Failed', error instanceof Error ? error.message : 'Failed to cancel job');
    }
  };

  if (jobError) {
    return (
      <div className="min-h-screen bg-background p-8">
        <div className="max-w-5xl mx-auto">
          <Link
            to="/jobs"
            className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6 transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Back to Jobs
          </Link>
          <div className="bg-danger-50 border border-danger-200 rounded-lg p-6">
            <div className="flex items-start gap-3">
              <AlertCircle className="w-6 h-6 text-danger-600 mt-0.5" />
              <div>
                <h3 className="text-lg font-semibold text-danger-900">Job not found</h3>
                <p className="text-danger-700 mt-1">
                  The job you're looking for doesn't exist or has been removed.
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (jobLoading || !job) {
    return <JobDetailSkeleton />;
  }

  const jobIsActive =
    job.status === 'Pending' ||
    job.status === 'Planning' ||
    job.status === 'Indexing';

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-5xl mx-auto p-8">
        {/* Back Button */}
        <Link
          to="/jobs"
          className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6 transition-colors"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Jobs
        </Link>

        {/* Header */}
        <div className="flex items-start justify-between mb-8">
          <div className="flex items-start gap-4">
            <div className="p-3 bg-primary-100 rounded-lg">
              <Briefcase className="w-8 h-8 text-primary-600" />
            </div>
            <div>
              <h1 className="text-3xl font-bold text-foreground">
                Indexing Job
                {isActive && (
                  <span className="ml-3 text-sm font-normal text-primary-600 bg-primary-50 px-3 py-1 rounded-full">
                    Auto-updating (5s)
                  </span>
                )}
              </h1>
              {project && (
                <Link
                  to={`/projects/${project.id}`}
                  className="text-muted-foreground mt-1 flex items-center gap-2 hover:text-primary-600 transition-colors"
                >
                  <FolderOpen className="w-4 h-4" />
                  {project.name}
                </Link>
              )}
              <div className="flex items-center gap-3 mt-3">
                {getStatusIcon(job.status!)}
                {getStatusBadge(job.status!)}
              </div>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            {jobIsActive && (
              <button
                onClick={() => setShowCancelDialog(true)}
                disabled={cancelJob.isPending}
                className="inline-flex items-center gap-2 px-4 py-2 bg-danger-600 text-white rounded-lg hover:bg-danger-700 transition-colors disabled:opacity-50"
              >
                <Ban className="w-4 h-4" />
                Cancel Job
              </button>
            )}
          </div>
        </div>

        {/* Error Message */}
        {job.errorMessage && (
          <div className="bg-danger-50 border border-danger-200 rounded-lg p-4 mb-6">
            <div className="flex items-start gap-3">
              <AlertCircle className="w-5 h-5 text-danger-600 mt-0.5" />
              <div>
                <p className="text-sm font-semibold text-danger-900">Error</p>
                <p className="text-sm text-danger-700 mt-1">{job.errorMessage}</p>
              </div>
            </div>
          </div>
        )}

        {/* Progress Bar (for active jobs) */}
        {jobIsActive && (
          <div className="bg-card border border-border rounded-lg p-6 mb-6">
            <h2 className="text-lg font-semibold text-foreground mb-4">Progress</h2>
            <div className="space-y-4">
              <div className="flex items-center justify-between text-sm mb-2">
                <span className="text-muted-foreground">
                  {job.processedFiles}/{job.totalFiles} files processed
                </span>
                <span className="font-medium text-foreground">
                  {job.progress?.toFixed(1)}%
                </span>
              </div>
              <div className="w-full bg-muted rounded-full h-3">
                <div
                  className="bg-primary-600 h-3 rounded-full transition-all duration-300"
                  style={{ width: `${job.progress}%` }}
                />
              </div>
              {job.currentOperation && (
                <p className="text-sm text-muted-foreground">
                  Current: {job.currentOperation}
                </p>
              )}
              {job.estimatedCompletion && (
                <p className="text-sm text-muted-foreground">
                  Estimated completion: {formatDate(job.estimatedCompletion)}
                </p>
              )}
            </div>
          </div>
        )}

        {/* Summary Metrics */}
        <div className="grid grid-cols-4 gap-4 mb-8">
          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-2">
              <FileText className="w-4 h-4 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">Files Processed</p>
            </div>
            <p className="text-2xl font-bold text-foreground">
              {job.processedFiles}/{job.totalFiles}
            </p>
          </div>

          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-2">
              <TrendingUp className="w-4 h-4 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">Chunks Created</p>
            </div>
            <p className="text-2xl font-bold text-foreground">
              {job.chunksCreated?.toLocaleString() || 0}
            </p>
          </div>

          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-2">
              <HardDrive className="w-4 h-4 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">Vectors Saved</p>
            </div>
            <p className="text-2xl font-bold text-foreground">
              {job.vectorsSaved?.toLocaleString() || 0}
            </p>
          </div>

          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-2">
              <Zap className="w-4 h-4 text-muted-foreground" />
              <p className="text-sm text-muted-foreground">Duration</p>
            </div>
            <p className="text-2xl font-bold text-foreground">
              {job.elapsed ? formatDuration(job.elapsed) : '0s'}
            </p>
          </div>
        </div>

        {/* File Statistics */}
        <div className="bg-card border border-border rounded-lg p-6 mb-6">
          <h2 className="text-lg font-semibold text-foreground mb-4">File Statistics</h2>
          <div className="grid grid-cols-2 md:grid-cols-5 gap-6">
            <div className="text-center">
              <div className="inline-flex items-center justify-center w-12 h-12 bg-success-100 rounded-full mb-2">
                <CheckCheck className="w-6 h-6 text-success-600" />
              </div>
              <p className="text-2xl font-bold text-success-600">{job.newFiles || 0}</p>
              <p className="text-sm text-muted-foreground">New Files</p>
            </div>

            <div className="text-center">
              <div className="inline-flex items-center justify-center w-12 h-12 bg-primary-100 rounded-full mb-2">
                <FileText className="w-6 h-6 text-primary-600" />
              </div>
              <p className="text-2xl font-bold text-primary-600">{job.changedFiles || 0}</p>
              <p className="text-sm text-muted-foreground">Changed</p>
            </div>

            <div className="text-center">
              <div className="inline-flex items-center justify-center w-12 h-12 bg-muted rounded-full mb-2">
                <MinusCircle className="w-6 h-6 text-muted-foreground" />
              </div>
              <p className="text-2xl font-bold text-muted-foreground">{job.skippedFiles || 0}</p>
              <p className="text-sm text-muted-foreground">Skipped</p>
            </div>

            <div className="text-center">
              <div className="inline-flex items-center justify-center w-12 h-12 bg-danger-100 rounded-full mb-2">
                <XOctagon className="w-6 h-6 text-danger-600" />
              </div>
              <p className="text-2xl font-bold text-danger-600">{job.errorFiles || 0}</p>
              <p className="text-sm text-muted-foreground">Errors</p>
            </div>

            <div className="text-center">
              <div className="inline-flex items-center justify-center w-12 h-12 bg-yellow-100 rounded-full mb-2">
                <AlertTriangle className="w-6 h-6 text-yellow-600" />
              </div>
              <p className="text-2xl font-bold text-yellow-600">0</p>
              <p className="text-sm text-muted-foreground">Warnings</p>
            </div>
          </div>
        </div>

        {/* Timeline */}
        <div className="bg-card border border-border rounded-lg p-6">
          <h2 className="text-lg font-semibold text-foreground mb-4 flex items-center gap-2">
            <Calendar className="w-5 h-5" />
            Timeline
          </h2>
          <div className="space-y-4">
            <div className="flex items-center justify-between py-2 border-b border-border">
              <span className="text-sm font-medium text-foreground">Started</span>
              <span className="text-sm text-muted-foreground">{formatDate(job.startedAt)}</span>
            </div>

            {job.completedAt && (
              <div className="flex items-center justify-between py-2 border-b border-border">
                <span className="text-sm font-medium text-foreground">Completed</span>
                <span className="text-sm text-muted-foreground">
                  {formatDate(job.completedAt)}
                </span>
              </div>
            )}

            <div className="flex items-center justify-between py-2 border-b border-border">
              <span className="text-sm font-medium text-foreground">Elapsed Time</span>
              <span className="text-sm text-muted-foreground">
                {job.elapsed ? formatDuration(job.elapsed) : '0s'}
              </span>
            </div>

            {job.estimatedCompletion && jobIsActive && (
              <div className="flex items-center justify-between py-2">
                <span className="text-sm font-medium text-foreground">Estimated Completion</span>
                <span className="text-sm text-muted-foreground">
                  {formatDate(job.estimatedCompletion)}
                </span>
              </div>
            )}
          </div>
        </div>

        {/* Back to Project Link */}
        {project && (
          <div className="mt-6 text-center">
            <Link
              to={`/projects/${project.id}`}
              className="inline-flex items-center gap-2 text-sm text-primary-600 hover:text-primary-700 font-medium"
            >
              View Project Details →
            </Link>
          </div>
        )}
      </div>

      {/* Cancel Job Confirmation Dialog */}
      <ConfirmDialog
        isOpen={showCancelDialog}
        onClose={() => setShowCancelDialog(false)}
        onConfirm={handleCancel}
        title="Cancel Indexing Job"
        message="Are you sure you want to cancel this indexing job? This action cannot be undone. You can restart indexing later from the Projects page."
        confirmText="Cancel Job"
        cancelText="Keep Running"
        variant="warning"
        isLoading={cancelJob.isPending}
      />
    </div>
  );
}
