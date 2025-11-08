import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { jobsApi } from '@/api';
import type { ListJobsRequest } from '@/api/types';

/**
 * Query keys for jobs
 */
export const jobKeys = {
  all: ['jobs'] as const,
  lists: () => [...jobKeys.all, 'list'] as const,
  list: (filters?: unknown) => [...jobKeys.lists(), filters] as const,
  details: () => [...jobKeys.all, 'detail'] as const,
  detail: (id: string) => [...jobKeys.details(), id] as const,
  byProject: (projectId: string) => [...jobKeys.all, 'project', projectId] as const,
  current: (projectId: string) => [...jobKeys.byProject(projectId), 'current'] as const,
  active: () => [...jobKeys.all, 'active'] as const,
};

/**
 * Hook to fetch a specific job
 */
export function useJob(jobId: string, refetchInterval?: number) {
  return useQuery({
    queryKey: jobKeys.detail(jobId),
    queryFn: () => jobsApi.get(jobId),
    enabled: !!jobId,
    refetchInterval, // For polling active jobs
    staleTime: 0,
  });
}

/**
 * Hook to fetch jobs by project
 */
export function useJobsByProject(projectId: string, limit = 10) {
  return useQuery({
    queryKey: [...jobKeys.byProject(projectId), { limit }],
    queryFn: () => jobsApi.listByProject(projectId, limit),
    enabled: !!projectId,
    staleTime: 10000, // 10 seconds
  });
}

/**
 * Hook to fetch current job for a project
 */
export function useCurrentJob(projectId: string, refetchInterval?: number) {
  return useQuery({
    queryKey: jobKeys.current(projectId),
    queryFn: () => jobsApi.getCurrent(projectId),
    enabled: !!projectId,
    refetchInterval, // For polling active jobs
    staleTime: 0,
  });
}

/**
 * Hook to fetch all jobs with filtering and pagination
 */
export function useAllJobs(request: ListJobsRequest = {}, refetchInterval?: number) {
  return useQuery({
    queryKey: jobKeys.list(request),
    queryFn: () => jobsApi.listAll(request),
    refetchInterval,
    staleTime: 5000, // 5 seconds
  });
}

/**
 * Hook to fetch all active jobs
 */
export function useActiveJobs(refetchInterval?: number) {
  return useQuery({
    queryKey: jobKeys.active(),
    queryFn: () => jobsApi.listActive(),
    refetchInterval, // Default 5000ms polling for dashboard
    staleTime: 0,
  });
}

/**
 * Hook to cancel a job
 */
export function useCancelJob() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (jobId: string) => jobsApi.cancel(jobId),
    onSuccess: (_, jobId) => {
      queryClient.invalidateQueries({ queryKey: jobKeys.detail(jobId) });
      queryClient.invalidateQueries({ queryKey: jobKeys.active() });
      queryClient.invalidateQueries({ queryKey: jobKeys.lists() });
    },
  });
}
