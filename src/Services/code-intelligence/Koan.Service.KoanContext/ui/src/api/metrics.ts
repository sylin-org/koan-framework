import { apiClient } from './client';

/**
 * Metrics API response types
 */
export interface MetricsSummary {
  projects: ProjectMetrics;
  chunks: ChunkMetrics;
  searches: SearchMetrics;
  performance: PerformanceMetrics;
  timestamp: string;
}

export interface ProjectMetrics {
  total: number;
  ready: number;
  indexing: number;
  failed: number;
  changeToday: number;
}

export interface ChunkMetrics {
  total: number;
  changeToday: number;
  changeTrend: string;
}

export interface SearchMetrics {
  today: number;
  last24h: number;
  perHour: number;
  changeTrend: string;
}

export interface PerformanceMetrics {
  avgLatencyMs: number;
  p95LatencyMs: number;
  p99LatencyMs: number;
  changeWeek: number;
}

export interface PerformanceTrends {
  period: string;
  startTime: string;
  endTime: string;
  dataPoints: PerformanceDataPoint[];
  timestamp: string;
}

export interface PerformanceDataPoint {
  timestamp: string;
  avgLatencyMs: number;
  p95LatencyMs: number;
  p99LatencyMs: number;
  requestCount: number;
}

export interface SystemHealth {
  healthy: boolean;
  status: string;
  checks: {
    database: {
      healthy: boolean;
      message: string;
    };
    projects: {
      healthy: boolean;
      total: number;
      failed: number;
      message: string;
    };
  };
  timestamp: string;
}

/**
 * Metrics API module
 */
export const metricsApi = {
  /**
   * Get dashboard summary metrics
   */
  async getSummary(): Promise<MetricsSummary> {
    const response = await apiClient.get<{ data: MetricsSummary }>('/metrics/summary');
    return response.data.data;
  },

  /**
   * Get performance metrics over a time period
   */
  async getPerformance(period: '1h' | '6h' | '24h' | '7d' | '30d' = '24h'): Promise<PerformanceTrends> {
    const response = await apiClient.get<{ data: PerformanceTrends }>(`/metrics/performance?period=${period}`);
    return response.data.data;
  },

  /**
   * Get system health status
   */
  async getHealth(): Promise<SystemHealth> {
    const response = await apiClient.get<SystemHealth>('/metrics/health');
    return response.data;
  },
};
