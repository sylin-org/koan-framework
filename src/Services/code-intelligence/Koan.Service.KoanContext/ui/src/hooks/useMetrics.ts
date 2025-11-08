import { useQuery } from '@tanstack/react-query';
import { metricsApi } from '@/api';

/**
 * Query keys for metrics
 */
export const metricsKeys = {
  all: ['metrics'] as const,
  summary: () => [...metricsKeys.all, 'summary'] as const,
  performance: (period?: string) => [...metricsKeys.all, 'performance', period] as const,
  health: () => [...metricsKeys.all, 'health'] as const,
};

/**
 * Hook to fetch metrics summary
 */
export function useMetricsSummary() {
  return useQuery({
    queryKey: metricsKeys.summary(),
    queryFn: () => metricsApi.getSummary(),
    staleTime: 30000, // 30 seconds (matches backend cache)
    refetchInterval: 30000, // Refresh every 30s
  });
}

/**
 * Hook to fetch performance trends
 */
export function usePerformanceTrends(period: '1h' | '6h' | '24h' | '7d' | '30d' = '24h') {
  return useQuery({
    queryKey: metricsKeys.performance(period),
    queryFn: () => metricsApi.getPerformance(period),
    staleTime: 30000,
    refetchInterval: 30000,
  });
}

/**
 * Hook to fetch system health
 */
export function useSystemHealth() {
  return useQuery({
    queryKey: metricsKeys.health(),
    queryFn: () => metricsApi.getHealth(),
    staleTime: 10000, // 10 seconds for health
    refetchInterval: 10000, // Check health every 10s
  });
}
