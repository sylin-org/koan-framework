import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useAllJobs, useActiveJobs, useCancelJob } from '@/hooks/useJobs';
import { useProjects } from '@/hooks/useProjects';
import { useToast } from '@/hooks/useToast';
import { type Job } from '@/api/types';
import ConfirmDialog from '@/components/ConfirmDialog';
import { JobCardSkeleton } from '@/components/Skeleton';
import {
  Briefcase,
  Loader2,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Clock,
  Ban,
  Filter,
  FolderOpen,
  ChevronRight,
} from 'lucide-react';

export default function JobsList() {
  const [statusFilter, setStatusFilter] = useState<string | 'all'>('all');
  const [projectFilter, setProjectFilter] = useState<string>('all');
  const [jobToCancel, setJobToCancel] = useState<Job | null>(null);
  const toast = useToast();

  // Fetch all jobs with filters (poll active jobs every 5s)
  const hasActiveFilters = statusFilter !== 'all' || projectFilter !== 'all';
  const { data: allJobsData, isLoading: allJobsLoading } = useAllJobs(
    {
      projectId: projectFilter !== 'all' ? projectFilter : undefined,
      status: statusFilter !== 'all' ? statusFilter : undefined,
      limit: 100,
    },
    hasActiveFilters ? undefined : 5000 // Poll every 5s if viewing all jobs
  );

  // Also fetch active jobs count for header indicator
  const { data: activeJobsData } = useActiveJobs(5000);
  const { data: projects } = useProjects();
  const cancelJob = useCancelJob();

  const allJobs = allJobsData?.jobs || [];
  const activeJobsCount = activeJobsData?.count || 0;
  const totalJobsCount = allJobsData?.totalCount || 0;

  // Get status icon and color
  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'Completed':
        return <CheckCircle2 className="w-5 h-5 text-success-600" />;
      case 'Failed':
        return <XCircle className="w-5 h-5 text-danger-600" />;
      case 'Cancelled':
        return <Ban className="w-5 h-5 text-muted-foreground" />;
      case 'Indexing':
        return <Loader2 className="w-5 h-5 text-primary-600 animate-spin" />;
      case 'Planning':
        return <Clock className="w-5 h-5 text-yellow-600" />;
      default:
        return <Clock className="w-5 h-5 text-muted-foreground" />;
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
      <span className={`px-2 py-1 rounded text-xs font-medium ${styles[status]}`}>
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

    if (hours > 0) return `${hours}h ${minutes}m`;
    if (minutes > 0) return `${minutes}m ${seconds}s`;
    return `${seconds}s`;
  };

  const formatTime = (dateString: string | null | undefined) => {
    if (!dateString) return 'N/A';
    const date = new Date(dateString);
    return date.toLocaleTimeString();
  };

  const isJobActive = (status: string) => {
    return status === 'Pending' || status === 'Planning' || status === 'Indexing';
  };

  // Jobs are already filtered by the backend, no need for client-side filtering
  const filteredJobs = allJobs;

  const handleCancelConfirm = async () => {
    if (!jobToCancel) return;

    try {
      await cancelJob.mutateAsync(jobToCancel.id);
      toast.success('Job Cancelled', 'Indexing job has been cancelled');
      setJobToCancel(null);
    } catch (error) {
      toast.error('Cancel Failed', error instanceof Error ? error.message : 'Failed to cancel job');
    }
  };

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-7xl mx-auto p-8">
        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-3xl font-bold text-foreground flex items-center gap-3">
              <Briefcase className="w-8 h-8" />
              Indexing Jobs
              {activeJobsCount > 0 && (
                <span className="flex items-center gap-2 text-sm font-normal text-primary-600 bg-primary-50 px-3 py-1 rounded-full">
                  <Loader2 className="w-3 h-3 animate-spin" />
                  {activeJobsCount} active
                </span>
              )}
            </h1>
            <p className="text-muted-foreground mt-2">
              Track and manage indexing jobs across all projects
            </p>
          </div>
        </div>

        {/* Stats Bar */}
        <div className="grid grid-cols-4 gap-4 mb-6">
          <div className="bg-card border border-border rounded-lg p-4">
            <p className="text-sm text-muted-foreground">Total Jobs</p>
            <p className="text-2xl font-bold text-foreground">{totalJobsCount}</p>
          </div>
          <div className="bg-card border border-border rounded-lg p-4">
            <p className="text-sm text-muted-foreground">Active</p>
            <p className="text-2xl font-bold text-primary-600">{activeJobsCount}</p>
          </div>
          <div className="bg-card border border-border rounded-lg p-4">
            <p className="text-sm text-muted-foreground">Completed</p>
            <p className="text-2xl font-bold text-success-600">
              {statusFilter === 'all' ? allJobs.filter((j) => j.status === 'Completed').length : allJobs.filter((j) => j.status === 'Completed').length}
            </p>
          </div>
          <div className="bg-card border border-border rounded-lg p-4">
            <p className="text-sm text-muted-foreground">Failed</p>
            <p className="text-2xl font-bold text-danger-600">
              {statusFilter === 'all' ? allJobs.filter((j) => j.status === 'Failed').length : allJobs.filter((j) => j.status === 'Failed').length}
            </p>
          </div>
        </div>

        {/* Filters */}
        <div className="bg-card border border-border rounded-lg p-4 mb-6">
          <div className="flex items-center gap-4">
            <Filter className="w-4 h-4 text-muted-foreground" />

            {/* Status Filter */}
            <div className="flex items-center gap-2">
              <label className="text-sm font-medium text-foreground">Status:</label>
              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value as string | 'all')}
                className="px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-background"
              >
                <option value="all">All Statuses</option>
                <option value={'Pending'}>Pending</option>
                <option value={'Planning'}>Planning</option>
                <option value={'Indexing'}>Indexing</option>
                <option value={'Completed'}>Completed</option>
                <option value={'Failed'}>Failed</option>
                <option value={'Cancelled'}>Cancelled</option>
              </select>
            </div>

            {/* Project Filter */}
            <div className="flex items-center gap-2">
              <label className="text-sm font-medium text-foreground">Project:</label>
              <select
                value={projectFilter}
                onChange={(e) => setProjectFilter(e.target.value)}
                className="px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-background"
              >
                <option value="all">All Projects</option>
                {projects?.map((project) => (
                  <option key={project.id} value={project.id}>
                    {project.name}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </div>

        {/* Jobs List */}
        <div className="bg-card border border-border rounded-lg overflow-hidden">
          {allJobsLoading ? (
            <div className="space-y-0 divide-y divide-border">
              {[1, 2, 3, 4].map((i) => (
                <JobCardSkeleton key={i} />
              ))}
            </div>
          ) : filteredJobs.length > 0 ? (
            <div className="space-y-0 divide-y divide-border">
              {filteredJobs.map((job) => {
                const project = projects?.find((p) => p.id === job.projectId);
                const active = isJobActive(job.status!);

                return (
                  <div
                    key={job.id}
                    className="p-6 hover:bg-muted/30 transition-colors"
                  >
                    {/* Header Row */}
                    <div className="flex items-start justify-between mb-4">
                      <div className="flex items-start gap-3">
                        {getStatusIcon(job.status!)}
                        <div>
                          <div className="flex items-center gap-2 mb-1">
                            <Link
                              to={`/jobs/${job.id}`}
                              className="font-medium text-foreground hover:text-primary-600 transition-colors"
                            >
                              {project?.name || job.projectId}
                            </Link>
                            <ChevronRight className="w-4 h-4 text-muted-foreground" />
                          </div>
                          <p className="text-sm text-muted-foreground">
                            {job.currentOperation || 'Processing...'}
                          </p>
                          {job.errorMessage && (
                            <p className="text-sm text-danger-600 mt-1">{job.errorMessage}</p>
                          )}
                        </div>
                      </div>
                      <div className="flex items-center gap-3">
                        {getStatusBadge(job.status!)}
                        {active && (
                          <button
                            onClick={() => setJobToCancel(job as Job)}
                            className="inline-flex items-center gap-1 px-3 py-1 text-sm text-danger-600 hover:bg-danger-50 rounded transition-colors"
                          >
                            <Ban className="w-3 h-3" />
                            Cancel
                          </button>
                        )}
                      </div>
                    </div>

                    {/* Progress Bar (for active jobs) */}
                    {active && (
                      <div className="mb-4">
                        <div className="flex items-center justify-between text-sm mb-2">
                          <span className="text-muted-foreground">
                            {job.processedFiles}/{job.totalFiles} files
                          </span>
                          <span className="font-medium text-foreground">
                            {job.progress?.toFixed(1)}%
                          </span>
                        </div>
                        <div className="w-full bg-muted rounded-full h-2">
                          <div
                            className="bg-primary-600 h-2 rounded-full transition-all duration-300"
                            style={{ width: `${job.progress}%` }}
                          />
                        </div>
                      </div>
                    )}

                    {/* Stats Grid */}
                    <div className="grid grid-cols-5 gap-4 text-sm">
                      <div>
                        <p className="text-muted-foreground">Files</p>
                        <p className="font-medium text-foreground">
                          {job.processedFiles}/{job.totalFiles}
                        </p>
                      </div>
                      <div>
                        <p className="text-muted-foreground">Chunks</p>
                        <p className="font-medium text-foreground">
                          {job.chunksCreated?.toLocaleString() || 0}
                        </p>
                      </div>
                      <div>
                        <p className="text-muted-foreground">Duration</p>
                        <p className="font-medium text-foreground">
                          {job.elapsed ? formatDuration(job.elapsed) : '0s'}
                        </p>
                      </div>
                      <div>
                        <p className="text-muted-foreground">Started</p>
                        <p className="font-medium text-foreground">
                          {formatTime(job.startedAt)}
                        </p>
                      </div>
                      <div>
                        <p className="text-muted-foreground">
                          {active ? 'ETA' : 'Completed'}
                        </p>
                        <p className="font-medium text-foreground">
                          {active
                            ? job.estimatedCompletion
                              ? formatTime(job.estimatedCompletion)
                              : 'Calculating...'
                            : job.completedAt
                            ? formatTime(job.completedAt)
                            : 'N/A'}
                        </p>
                      </div>
                    </div>

                    {/* Additional Stats for Completed Jobs */}
                    {!active && (
                      <div className="grid grid-cols-5 gap-4 text-sm mt-3">
                        <div>
                          <p className="text-muted-foreground">New Files</p>
                          <p className="font-medium text-success-600">
                            {job.newFiles || 0}
                          </p>
                        </div>
                        <div>
                          <p className="text-muted-foreground">Changed</p>
                          <p className="font-medium text-primary-600">
                            {job.changedFiles || 0}
                          </p>
                        </div>
                        <div>
                          <p className="text-muted-foreground">Skipped</p>
                          <p className="font-medium text-muted-foreground">
                            {job.skippedFiles || 0}
                          </p>
                        </div>
                        <div>
                          <p className="text-muted-foreground">Errors</p>
                          <p className="font-medium text-danger-600">
                            {job.errorFiles || 0}
                          </p>
                        </div>
                        <div>
                          <p className="text-muted-foreground">Vectors</p>
                          <p className="font-medium text-foreground">
                            {job.vectorsSaved?.toLocaleString() || 0}
                          </p>
                        </div>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          ) : (
            <div className="text-center py-12">
              <Briefcase className="w-12 h-12 text-muted-foreground mx-auto mb-3" />
              <p className="text-muted-foreground mb-4">
                {statusFilter !== 'all' || projectFilter !== 'all'
                  ? 'No jobs match your filters'
                  : 'No indexing jobs yet'}
              </p>
              {statusFilter === 'all' && projectFilter === 'all' && (
                <Link
                  to="/projects"
                  className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
                >
                  <FolderOpen className="w-4 h-4" />
                  Create a Project to Start
                </Link>
              )}
            </div>
          )}
        </div>

        {/* Footer Stats */}
        {filteredJobs.length > 0 && (
          <div className="mt-4 text-sm text-muted-foreground text-center">
            Showing {filteredJobs.length} of {totalJobsCount} jobs
            {allJobsData?.hasMore && (
              <span className="ml-2 text-primary-600">(more available, increase limit to see all)</span>
            )}
          </div>
        )}

        {/* Info Banner for Active Jobs */}
        {activeJobsCount > 0 && (
          <div className="mt-6 bg-primary-50 border border-primary-200 rounded-lg p-4">
            <div className="flex items-start gap-3">
              <AlertCircle className="w-5 h-5 text-primary-600 mt-0.5" />
              <div>
                <p className="text-sm font-medium text-primary-900">
                  Active jobs are polling every 5 seconds
                </p>
                <p className="text-sm text-primary-700 mt-1">
                  This page automatically updates to show real-time progress. Click on any job to see detailed logs and file processing status.
                </p>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Cancel Job Confirmation Dialog */}
      <ConfirmDialog
        isOpen={!!jobToCancel}
        onClose={() => setJobToCancel(null)}
        onConfirm={handleCancelConfirm}
        title="Cancel Indexing Job"
        message={`Are you sure you want to cancel this indexing job? This action cannot be undone. You can restart indexing later from the Projects page.`}
        confirmText="Cancel Job"
        cancelText="Keep Running"
        variant="warning"
        isLoading={cancelJob.isPending}
      />
    </div>
  );
}
