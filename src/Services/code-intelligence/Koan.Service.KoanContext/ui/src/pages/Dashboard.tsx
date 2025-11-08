import { Link } from 'react-router-dom';
import { useProjects } from '@/hooks/useProjects';
import { useActiveJobs } from '@/hooks/useJobs';
import { useMetricsSummary, useSystemHealth } from '@/hooks/useMetrics';
import { DashboardSkeleton } from '@/components/Skeleton';
import {
  Database,
  Activity,
  FolderOpen,
  AlertCircle,
  CheckCircle2,
  Loader2,
  XCircle,
  Clock
} from 'lucide-react';

export default function Dashboard() {
  const { data: projects, isLoading: projectsLoading, error: projectsError } = useProjects();
  const { data: activeJobsData } = useActiveJobs(5000); // Poll every 5s
  const { data: metrics, isLoading: metricsLoading } = useMetricsSummary();
  const { data: health } = useSystemHealth();

  const activeJobs = activeJobsData?.jobs || [];
  const activeJobsCount = activeJobsData?.count || 0;

  // Format bytes to human readable
  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
  };

  // Format duration
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

  const getJobStatusColor = (status: string) => {
    switch (status) {
      case 'Completed':
        return 'text-success-600 bg-success-50';
      case 'Failed':
        return 'text-danger-600 bg-danger-50';
      case 'Cancelled':
        return 'text-muted-foreground bg-muted';
      default:
        return 'text-primary-600 bg-primary-50';
    }
  };

  if (projectsError) {
    return (
      <div className="min-h-screen bg-background p-8">
        <div className="max-w-7xl mx-auto">
          <div className="bg-danger-50 border border-danger-200 rounded-lg p-6">
            <div className="flex items-start gap-3">
              <AlertCircle className="w-6 h-6 text-danger-600 mt-0.5" />
              <div>
                <h3 className="text-lg font-semibold text-danger-900">Failed to load dashboard</h3>
                <p className="text-danger-700 mt-1">
                  {projectsError instanceof Error ? projectsError.message : 'Unknown error occurred'}
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (projectsLoading || metricsLoading) {
    return <DashboardSkeleton />;
  }

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-7xl mx-auto p-8">
        {/* System Health Banner */}
        {health && (
          <div className={`mb-8 border rounded-lg p-4 ${
            health.healthy
              ? 'bg-success-50 border-success-200'
              : 'bg-danger-50 border-danger-200'
          }`}>
            <div className="flex items-center gap-3">
              {health.healthy ? (
                <CheckCircle2 className="w-5 h-5 text-success-600" />
              ) : (
                <AlertCircle className="w-5 h-5 text-danger-600" />
              )}
              <div>
                <span className={`font-medium ${
                  health.healthy ? 'text-success-900' : 'text-danger-900'
                }`}>
                  System Health: {health.status === 'healthy' ? 'All Systems Operational' : 'Degraded'}
                </span>
                {!health.healthy && health.checks.projects.failed > 0 && (
                  <p className="text-sm text-danger-700 mt-1">
                    {health.checks.projects.message}
                  </p>
                )}
              </div>
            </div>
          </div>
        )}

        {/* 3-Column Metrics */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
          {/* METRICS Column */}
          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-lg font-semibold text-foreground mb-6 flex items-center gap-2">
              <Database className="w-5 h-5" />
              Metrics
            </h2>
            {metricsLoading ? (
              <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
            ) : metrics ? (
              <div className="space-y-4">
                <div>
                  <p className="text-sm text-muted-foreground">Projects</p>
                  <p className="text-2xl font-bold text-foreground">{metrics.projects.total}</p>
                  <p className="text-xs text-muted-foreground mt-1">
                    {metrics.projects.ready} ready · {metrics.projects.indexing} indexing · {metrics.projects.failed} failed
                  </p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Chunks</p>
                  <p className="text-2xl font-bold text-foreground">{metrics.chunks.total.toLocaleString()}</p>
                  {metrics.chunks.changeToday > 0 && (
                    <p className="text-xs text-success-600 mt-1">
                      +{metrics.chunks.changeToday.toLocaleString()} today
                    </p>
                  )}
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Indexed</p>
                  <p className="text-2xl font-bold text-foreground">
                    {formatBytes(projects?.reduce((sum, p) => sum + p.indexedBytes, 0) || 0)}
                  </p>
                </div>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">No metrics available</p>
            )}
          </div>

          {/* PERFORMANCE Column */}
          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-lg font-semibold text-foreground mb-6 flex items-center gap-2">
              <Activity className="w-5 h-5" />
              Performance
            </h2>
            {metricsLoading ? (
              <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
            ) : metrics?.performance ? (
              <div className="space-y-4">
                {metrics.performance.avgLatencyMs > 0 ? (
                  <>
                    <div>
                      <p className="text-sm text-muted-foreground">Avg Latency</p>
                      <p className="text-2xl font-bold text-foreground">{metrics.performance.avgLatencyMs}ms</p>
                    </div>
                    <div>
                      <p className="text-sm text-muted-foreground">P95 Latency</p>
                      <p className="text-2xl font-bold text-foreground">{metrics.performance.p95LatencyMs}ms</p>
                    </div>
                    <div>
                      <p className="text-sm text-muted-foreground">P99 Latency</p>
                      <p className="text-2xl font-bold text-foreground">{metrics.performance.p99LatencyMs}ms</p>
                    </div>
                  </>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    No performance data yet. Performance metrics will appear after search activity.
                  </p>
                )}
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">No performance data available</p>
            )}
          </div>

          {/* PROJECTS Column */}
          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-lg font-semibold text-foreground mb-6 flex items-center gap-2">
              <FolderOpen className="w-5 h-5" />
              Projects
            </h2>
            {projectsLoading ? (
              <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
            ) : projects && projects.length > 0 ? (
              <div className="space-y-3">
                {projects.slice(0, 3).map((project) => (
                  <Link
                    key={project.id}
                    to={`/projects/${project.id}`}
                    className="block p-3 border border-border rounded-lg hover:border-primary-300 hover:bg-primary-50/50 transition-all"
                  >
                    <div className="flex items-center gap-2 mb-1">
                      {getStatusIcon(project.status)}
                      <span className="font-medium text-sm text-foreground">{project.name}</span>
                    </div>
                    <p className="text-xs text-muted-foreground">
                      {project.documentCount.toLocaleString()} chunks
                    </p>
                  </Link>
                ))}
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">No projects yet</p>
            )}
          </div>
        </div>

        {/* Quick Actions */}
        <div className="bg-card border border-border rounded-lg p-6 mb-8">
          <h2 className="text-xl font-semibold text-foreground mb-4">Quick Actions</h2>
          <div className="flex flex-wrap gap-3">
            <Link
              to="/projects"
              className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
            >
              <FolderOpen className="w-4 h-4" />
              Index New Project
            </Link>
            <Link
              to="/"
              className="inline-flex items-center gap-2 px-4 py-2 border border-border rounded-lg hover:border-primary-300 hover:bg-primary-50 transition-colors"
            >
              <Database className="w-4 h-4" />
              Search All
            </Link>
            <Link
              to="/dashboard"
              className="inline-flex items-center gap-2 px-4 py-2 border border-border rounded-lg hover:border-primary-300 hover:bg-primary-50 transition-colors"
            >
              <Activity className="w-4 h-4" />
              View Analytics
            </Link>
          </div>
        </div>

        {/* Active Jobs List */}
        {activeJobsCount > 0 && (
          <div className="bg-card border border-border rounded-lg p-6 mb-8">
            <h2 className="text-xl font-semibold text-foreground mb-4 flex items-center gap-2">
              <Loader2 className="w-5 h-5 text-primary-600 animate-spin" />
              Active Indexing Jobs
            </h2>
            <div className="space-y-4">
              {activeJobs.map((job) => (
                <div key={job.id} className="border border-border rounded-lg p-4">
                  <div className="flex items-start justify-between mb-3">
                    <div>
                      <Link
                        to={`/jobs/${job.id}`}
                        className="font-medium text-foreground hover:text-primary-600 transition-colors"
                      >
                        {job.projectId}
                      </Link>
                      <p className="text-sm text-muted-foreground mt-1">
                        {job.currentOperation || 'Processing...'}
                      </p>
                    </div>
                    <span className={`px-3 py-1 rounded-full text-xs font-medium ${getJobStatusColor(job.status!)}`}>
                      {job.status!}
                    </span>
                  </div>

                  {/* Progress Bar */}
                  <div className="mb-3">
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

                  {/* Job Stats */}
                  <div className="grid grid-cols-4 gap-4 text-sm">
                    <div>
                      <p className="text-muted-foreground">Chunks</p>
                      <p className="font-medium text-foreground">{job.chunksCreated?.toLocaleString()}</p>
                    </div>
                    <div>
                      <p className="text-muted-foreground">Elapsed</p>
                      <p className="font-medium text-foreground">
                        {job.elapsed ? formatDuration(job.elapsed) : '0s'}
                      </p>
                    </div>
                    <div>
                      <p className="text-muted-foreground">Started</p>
                      <p className="font-medium text-foreground">
                        {job.startedAt ? new Date(job.startedAt).toLocaleTimeString() : 'N/A'}
                      </p>
                    </div>
                    <div>
                      <p className="text-muted-foreground">ETA</p>
                      <p className="font-medium text-foreground">
                        {job.estimatedCompletion
                          ? new Date(job.estimatedCompletion).toLocaleTimeString()
                          : 'Calculating...'}
                      </p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Recent Projects */}
        <div className="bg-card border border-border rounded-lg p-6">
          <h2 className="text-xl font-semibold text-foreground mb-4">Projects</h2>

          {projectsLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="w-8 h-8 text-primary-600 animate-spin" />
            </div>
          ) : projects && projects.length > 0 ? (
            <div className="space-y-3">
              {projects.slice(0, 10).map((project) => (
                <Link
                  key={project.id}
                  to={`/projects/${project.id}`}
                  className="flex items-center justify-between p-4 border border-border rounded-lg hover:border-primary-300 hover:bg-primary-50/50 transition-all"
                >
                  <div className="flex items-center gap-4 flex-1">
                    <div>{getStatusIcon(project.status)}</div>
                    <div className="flex-1">
                      <h3 className="font-medium text-foreground">{project.name}</h3>
                      <p className="text-sm text-muted-foreground">{project.rootPath}</p>
                    </div>
                  </div>
                  <div className="text-right">
                    <p className="text-sm font-medium text-foreground">
                      {project.documentCount.toLocaleString()} docs
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {project.lastIndexed
                        ? new Date(project.lastIndexed).toLocaleDateString()
                        : 'Never indexed'}
                    </p>
                  </div>
                </Link>
              ))}
              {projects.length > 10 && (
                <Link
                  to="/projects"
                  className="block text-center py-3 text-sm text-primary-600 hover:text-primary-700 transition-colors"
                >
                  View all {projects.length} projects →
                </Link>
              )}
            </div>
          ) : (
            <div className="text-center py-12">
              <FolderOpen className="w-12 h-12 text-muted-foreground mx-auto mb-3" />
              <p className="text-muted-foreground mb-4">No projects yet</p>
              <Link
                to="/projects"
                className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
              >
                Create Your First Project
              </Link>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
