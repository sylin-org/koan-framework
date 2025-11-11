import { Link } from 'react-router-dom';
import {
  useDashboardOverview,
  useOutboxHealth,
  useComponentHealth,
  useJobSystemMetrics,
  useVectorDbMetrics,
  useStorageMetrics,
  useSearchPerformance,
} from '@/hooks/useEnhancedMetrics';
import {
  AlertCircle,
  CheckCircle2,
  AlertTriangle,
  Database,
  Activity,
  HardDrive,
  Inbox,
  PlayCircle,
  Search,
  TrendingUp,
  Loader2,
} from 'lucide-react';

export default function MonitoringDashboard() {
  const { data: overview } = useDashboardOverview();
  const { data: outbox } = useOutboxHealth();
  const { data: components } = useComponentHealth();
  const { data: jobs } = useJobSystemMetrics();
  const { data: vectorDb } = useVectorDbMetrics();
  const { data: storage } = useStorageMetrics();
  const { data: searchPerf } = useSearchPerformance('1h');

  // Helper functions
  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
  };

  const getHealthIcon = (status: string) => {
    switch (status) {
      case 'healthy':
        return <CheckCircle2 className="w-5 h-5 text-success-600" />;
      case 'warning':
        return <AlertTriangle className="w-5 h-5 text-warning-600" />;
      case 'critical':
        return <AlertCircle className="w-5 h-5 text-danger-600" />;
      default:
        return <AlertCircle className="w-5 h-5 text-muted-foreground" />;
    }
  };

  const getHealthColor = (status: string) => {
    switch (status) {
      case 'healthy':
        return 'border-success-200 bg-success-50';
      case 'warning':
        return 'border-warning-200 bg-warning-50';
      case 'critical':
        return 'border-danger-200 bg-danger-50';
      default:
        return 'border-border bg-muted';
    }
  };

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-7xl mx-auto p-8">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-3xl font-bold text-foreground">System Monitoring</h1>
          <p className="text-muted-foreground mt-2">
            Real-time system health and performance metrics
          </p>
        </div>

        {/* Critical Alerts Banner */}
        {overview?.data?.criticalAlerts && overview.data.criticalAlerts.length > 0 && (
          <div className="mb-8 border border-danger-200 rounded-lg p-4 bg-danger-50">
            <div className="flex items-start gap-3">
              <AlertCircle className="w-6 h-6 text-danger-600 mt-0.5 flex-shrink-0" />
              <div className="flex-1">
                <h3 className="text-lg font-semibold text-danger-900">
                  {overview.data.criticalAlerts.length} Critical Alert{overview.data.criticalAlerts.length > 1 ? 's' : ''}
                </h3>
                <div className="mt-2 space-y-2">
                  {overview.data.criticalAlerts.map((alert, idx) => (
                    <div key={idx} className="text-sm text-danger-700">
                      <span className="font-medium">[{alert.type}]</span> {alert.message}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
        )}

        {/* P0 Metrics Row */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
          {/* Outbox Queue Health */}
          <div className={`border rounded-lg p-6 ${getHealthColor(outbox?.healthStatus || 'unknown')}`}>
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-lg font-semibold text-foreground flex items-center gap-2">
                <Inbox className="w-5 h-5" />
                Outbox Queue
              </h2>
              {getHealthIcon(outbox?.healthStatus || 'unknown')}
            </div>

            {outbox ? (
              <div className="space-y-3">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <p className="text-sm text-muted-foreground">Pending</p>
                    <p className={`text-2xl font-bold ${outbox.pendingCount > 100 ? 'text-danger-600' : 'text-foreground'}`}>
                      {outbox.pendingCount}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-muted-foreground">Processing Rate</p>
                    <p className="text-2xl font-bold text-foreground">
                      {outbox.processingRatePerSecond.toFixed(1)}/s
                    </p>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <p className="text-sm text-muted-foreground">Oldest Age</p>
                    <p className={`text-lg font-medium ${outbox.oldestAgeSeconds > 60 ? 'text-warning-600' : 'text-foreground'}`}>
                      {Math.round(outbox.oldestAgeSeconds)}s
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-muted-foreground">Dead Letter</p>
                    <p className={`text-lg font-medium ${outbox.deadLetterCount > 0 ? 'text-danger-600' : 'text-success-600'}`}>
                      {outbox.deadLetterCount}
                    </p>
                  </div>
                </div>
              </div>
            ) : (
              <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
            )}
          </div>

          {/* Component Health */}
          <div className={`border rounded-lg p-6 ${getHealthColor(components?.overallHealthy ? 'healthy' : 'critical')}`}>
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-lg font-semibold text-foreground flex items-center gap-2">
                <Activity className="w-5 h-5" />
                Component Health
              </h2>
              {getHealthIcon(components?.overallHealthy ? 'healthy' : 'critical')}
            </div>

            {components ? (
              <div className="space-y-2">
                {components.components.map((component) => (
                  <div key={component.name} className="flex items-center justify-between py-2 border-b border-border/50 last:border-0">
                    <div className="flex items-center gap-2">
                      {getHealthIcon(component.status)}
                      <span className="font-medium text-foreground">{component.name}</span>
                    </div>
                    <div className="text-right">
                      {component.latencyMs && (
                        <span className="text-sm text-muted-foreground">{component.latencyMs}ms</span>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
            )}
          </div>
        </div>

        {/* System Metrics Row */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
          {/* Job System */}
          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-lg font-semibold text-foreground mb-4 flex items-center gap-2">
              <PlayCircle className="w-5 h-5" />
              Job System
            </h2>

            {jobs ? (
              <div className="space-y-3">
                <div>
                  <p className="text-sm text-muted-foreground">Active Jobs</p>
                  <p className="text-2xl font-bold text-foreground">{jobs.activeJobsCount}</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Queued</p>
                  <p className="text-lg font-medium text-foreground">{jobs.queuedJobsCount}</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Success Rate (24h)</p>
                  <p className={`text-lg font-medium ${jobs.successRate24h < 90 ? 'text-warning-600' : 'text-success-600'}`}>
                    {jobs.successRate24h.toFixed(1)}%
                  </p>
                </div>
                <div className="pt-2 border-t border-border">
                  <p className="text-xs text-muted-foreground">Throughput</p>
                  <p className="text-sm font-medium text-foreground">
                    {jobs.avgChunksPerSecond.toFixed(1)} chunks/s
                  </p>
                </div>
              </div>
            ) : (
              <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
            )}
          </div>

          {/* Vector DB */}
          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-lg font-semibold text-foreground mb-4 flex items-center gap-2">
              <Database className="w-5 h-5" />
              Vector Database
            </h2>

            {vectorDb ? (
              <div className="space-y-3">
                <div>
                  <p className="text-sm text-muted-foreground">Collections</p>
                  <p className="text-2xl font-bold text-foreground">{vectorDb.collectionCount}</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Total Vectors</p>
                  <p className="text-lg font-medium text-foreground">
                    {vectorDb.totalVectors.toLocaleString()}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Storage Size</p>
                  <p className="text-lg font-medium text-foreground">
                    {formatBytes(vectorDb.estimatedSizeBytes)}
                  </p>
                </div>
                <div className="pt-2 border-t border-border flex items-center gap-2">
                  <TrendingUp className="w-4 h-4 text-success-600" />
                  <p className="text-xs text-muted-foreground">
                    +{vectorDb.growthRatePerDay.toLocaleString()} vectors/day
                  </p>
                </div>
              </div>
            ) : (
              <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
            )}
          </div>

          {/* Storage */}
          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-lg font-semibold text-foreground mb-4 flex items-center gap-2">
              <HardDrive className="w-5 h-5" />
              Storage
            </h2>

            {storage ? (
              <div className="space-y-3">
                <div>
                  <p className="text-sm text-muted-foreground">Database Size</p>
                  <p className="text-2xl font-bold text-foreground">
                    {formatBytes(storage.estimatedDbSizeBytes)}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Total Chunks</p>
                  <p className="text-lg font-medium text-foreground">
                    {storage.totalChunks.toLocaleString()}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Total Files</p>
                  <p className="text-lg font-medium text-foreground">
                    {storage.totalFiles.toLocaleString()}
                  </p>
                </div>
                <div className="pt-2 border-t border-border">
                  <p className="text-xs text-muted-foreground">Index Freshness</p>
                  <div className="flex gap-3 mt-1 text-xs">
                    <span className="text-success-600">{storage.freshProjects} fresh</span>
                    <span className="text-warning-600">{storage.staleProjects} stale</span>
                    {storage.veryStaleProjects > 0 && (
                      <span className="text-danger-600">{storage.veryStaleProjects} very stale</span>
                    )}
                  </div>
                </div>
              </div>
            ) : (
              <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
            )}
          </div>
        </div>

        {/* Search Performance */}
        <div className="bg-card border border-border rounded-lg p-6 mb-8">
          <h2 className="text-xl font-semibold text-foreground mb-4 flex items-center gap-2">
            <Search className="w-5 h-5" />
            Search Performance (Last Hour)
          </h2>

          {searchPerf ? (
            searchPerf.totalQueries > 0 ? (
              <div className="grid grid-cols-2 md:grid-cols-4 gap-6">
                <div>
                  <p className="text-sm text-muted-foreground">Total Queries</p>
                  <p className="text-2xl font-bold text-foreground">{searchPerf.totalQueries}</p>
                  <p className="text-xs text-success-600 mt-1">
                    {searchPerf.successfulQueries} successful
                  </p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Avg Latency</p>
                  <p className="text-2xl font-bold text-foreground">{Math.round(searchPerf.avgLatencyMs)}ms</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">P95 Latency</p>
                  <p className={`text-2xl font-bold ${searchPerf.p95LatencyMs > 1000 ? 'text-warning-600' : 'text-foreground'}`}>
                    {Math.round(searchPerf.p95LatencyMs)}ms
                  </p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">P99 Latency</p>
                  <p className="text-2xl font-bold text-foreground">{Math.round(searchPerf.p99LatencyMs)}ms</p>
                </div>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">
                No search queries recorded in the last hour. Metrics will appear after search activity.
              </p>
            )
          ) : (
            <Loader2 className="w-6 h-6 text-primary-600 animate-spin" />
          )}
        </div>

        {/* Quick Links */}
        <div className="bg-card border border-border rounded-lg p-6">
          <h2 className="text-xl font-semibold text-foreground mb-4">Quick Links</h2>
          <div className="flex flex-wrap gap-3">
            <Link
              to="/dashboard"
              className="inline-flex items-center gap-2 px-4 py-2 border border-border rounded-lg hover:border-primary-300 hover:bg-primary-50 transition-colors"
            >
              <Activity className="w-4 h-4" />
              Main Dashboard
            </Link>
            <Link
              to="/projects"
              className="inline-flex items-center gap-2 px-4 py-2 border border-border rounded-lg hover:border-primary-300 hover:bg-primary-50 transition-colors"
            >
              <Database className="w-4 h-4" />
              Projects
            </Link>
            <Link
              to="/"
              className="inline-flex items-center gap-2 px-4 py-2 border border-border rounded-lg hover:border-primary-300 hover:bg-primary-50 transition-colors"
            >
              <Search className="w-4 h-4" />
              Search
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
