import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/api';

// Type definitions
export interface VectorQueueHealthMetrics {
  pendingCount: number;
  failedCount: number;
  retryCount: number;
  oldestAgeSeconds: number;
  processingRatePerSecond: number;
  byProject: Array<{
    projectId: string;
    pendingCount: number;
    retryingCount: number;
    failedCount: number;
    oldestAgeSeconds: number;
  }>;
  healthStatus: 'healthy' | 'warning' | 'critical' | 'unknown';
  timestamp: string;
}

export interface ComponentHealth {
  name: string;
  status: 'healthy' | 'warning' | 'critical' | 'unknown';
  message: string;
  latencyMs?: number;
  lastChecked: string;
}

export interface ComponentHealthMetrics {
  overallHealthy: boolean;
  components: ComponentHealth[];
  timestamp: string;
}

export interface JobSystemMetrics {
  activeJobsCount: number;
  queuedJobsCount: number;
  completedLast24h: number;
  failedLast24h: number;
  successRate24h: number;
  throughputJobsPerHour: number;
  avgChunksPerSecond: number;
  avgFilesPerSecond: number;
  timestamp: string;
}

export interface VectorDbMetrics {
  collectionCount: number;
  totalVectors: number;
  estimatedSizeBytes: number;
  growthRatePerDay: number;
  collections: Array<{
    projectId: string;
    projectName: string;
    vectorCount: number;
    estimatedSizeBytes: number;
  }>;
  timestamp: string;
}

export interface StorageMetrics {
  estimatedDbSizeBytes: number;
  totalChunks: number;
  totalFiles: number;
  totalIndexedBytes: number;
  freshProjects: number;
  staleProjects: number;
  veryStaleProjects: number;
  chunksAddedToday: number;
  timestamp: string;
}

export interface SearchPerformanceStats {
  totalQueries: number;
  successfulQueries: number;
  failedQueries: number;
  avgLatencyMs: number;
  p50LatencyMs: number;
  p95LatencyMs: number;
  p99LatencyMs: number;
  period: string;
}

export interface Alert {
  type: string;
  severity: 'critical' | 'warning';
  message: string;
  metadata?: Record<string, unknown>;
}

export interface DashboardOverview {
  vectorQueueHealth: {
    status: string;
    pending: number;
    failed: number;
    processingRate: number;
  };
  componentHealth: {
    healthy: boolean;
    degradedCount: number;
    components: Array<{
      name: string;
      status: string;
      latencyMs?: number;
    }>;
  };
  jobSystem: {
    active: number;
    queued: number;
    successRate24h: number;
    failed24h: number;
  };
  searchPerformance: {
    totalQueries: number;
    avgLatencyMs: number;
    p95LatencyMs: number;
    failureRate: number;
  };
  criticalAlerts: Alert[];
}

/**
 * Hook for fetching vector queue health metrics
 * Refreshes every 5 seconds for P0 monitoring
 */
export function useVectorQueueHealth(pollInterval = 5000) {
  return useQuery({
    queryKey: ['metrics', 'vector-queue'],
    queryFn: async () => {
      const response = await apiClient.get<{ data: VectorQueueHealthMetrics; metadata: any }>(
        '/metrics/vector-queue'
      );
      return response.data.data;
    },
    refetchInterval: pollInterval,
    staleTime: 4000,
  });
}

/**
 * Hook for fetching component health matrix
 * Refreshes every 5 seconds for P0 monitoring
 */
export function useComponentHealth(pollInterval = 5000) {
  return useQuery({
    queryKey: ['metrics', 'components'],
    queryFn: async () => {
      const response = await apiClient.get<{ data: ComponentHealthMetrics; metadata: any }>('/metrics/components');
      return response.data.data;
    },
    refetchInterval: pollInterval,
    staleTime: 4000,
  });
}

/**
 * Hook for fetching job system metrics
 */
export function useJobSystemMetrics(pollInterval = 10000) {
  return useQuery({
    queryKey: ['metrics', 'jobs'],
    queryFn: async () => {
      const response = await apiClient.get<{ data: JobSystemMetrics; metadata: any }>('/metrics/jobs');
      return response.data.data;
    },
    refetchInterval: pollInterval,
    staleTime: 8000,
  });
}

/**
 * Hook for fetching vector database metrics
 */
export function useVectorDbMetrics(pollInterval = 30000) {
  return useQuery({
    queryKey: ['metrics', 'vector-db'],
    queryFn: async () => {
      const response = await apiClient.get<{ data: VectorDbMetrics; metadata: any }>('/metrics/vector-db');
      return response.data.data;
    },
    refetchInterval: pollInterval,
    staleTime: 25000,
  });
}

/**
 * Hook for fetching storage metrics
 */
export function useStorageMetrics(pollInterval = 30000) {
  return useQuery({
    queryKey: ['metrics', 'storage'],
    queryFn: async () => {
      const response = await apiClient.get<{ data: StorageMetrics; metadata: any }>('/metrics/storage');
      return response.data.data;
    },
    refetchInterval: pollInterval,
    staleTime: 25000,
  });
}

/**
 * Hook for fetching search performance statistics
 */
export function useSearchPerformance(period: '1h' | '6h' | '24h' = '1h', pollInterval = 30000) {
  return useQuery({
    queryKey: ['metrics', 'search-performance', period],
    queryFn: async () => {
      const response = await apiClient.get<{ data: SearchPerformanceStats; metadata: any }>(
        `/metrics/search-performance?period=${period}`
      );
      return response.data.data;
    },
    refetchInterval: pollInterval,
    staleTime: 25000,
  });
}

/**
 * Hook for fetching comprehensive dashboard overview
 * Single endpoint that aggregates critical metrics and alerts
 */
export function useDashboardOverview(pollInterval = 10000) {
  return useQuery({
    queryKey: ['metrics', 'dashboard'],
    queryFn: async () => {
      const response = await apiClient.get<{ data: DashboardOverview; metadata: any }>('/metrics/dashboard');
      return response.data;
    },
    refetchInterval: pollInterval,
    staleTime: 8000,
  });
}
