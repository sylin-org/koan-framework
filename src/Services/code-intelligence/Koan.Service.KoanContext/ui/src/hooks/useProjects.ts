import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { projectsApi, type CreateProjectRequest } from '@/api';

/**
 * Query keys for projects
 */
export const projectKeys = {
  all: ['projects'] as const,
  lists: () => [...projectKeys.all, 'list'] as const,
  list: (filters?: unknown) => [...projectKeys.lists(), filters] as const,
  details: () => [...projectKeys.all, 'detail'] as const,
  detail: (id: string) => [...projectKeys.details(), id] as const,
  status: (id: string) => [...projectKeys.detail(id), 'status'] as const,
  health: (id: string) => [...projectKeys.detail(id), 'health'] as const,
};

/**
 * Hook to fetch all projects
 */
export function useProjects() {
  return useQuery({
    queryKey: projectKeys.lists(),
    queryFn: () => projectsApi.list(),
    staleTime: 30000, // 30 seconds
  });
}

/**
 * Hook to fetch active projects
 */
export function useActiveProjects() {
  return useQuery({
    queryKey: [...projectKeys.lists(), 'active'],
    queryFn: () => projectsApi.getActive(),
    staleTime: 30000,
  });
}

/**
 * Hook to fetch a single project
 */
export function useProject(id: string) {
  return useQuery({
    queryKey: projectKeys.detail(id),
    queryFn: () => projectsApi.get(id),
    enabled: !!id,
    staleTime: 30000,
  });
}

/**
 * Hook to fetch project status
 */
export function useProjectStatus(id: string, refetchInterval?: number) {
  return useQuery({
    queryKey: projectKeys.status(id),
    queryFn: () => projectsApi.getStatus(id),
    enabled: !!id,
    refetchInterval, // Pass undefined or number (ms) for polling
    staleTime: 0, // Always fresh for status
  });
}

/**
 * Hook to fetch project health
 */
export function useProjectHealth(id: string) {
  return useQuery({
    queryKey: projectKeys.health(id),
    queryFn: () => projectsApi.getHealth(id),
    enabled: !!id,
    staleTime: 30000,
  });
}

/**
 * Hook to create a project
 */
export function useCreateProject() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateProjectRequest) => projectsApi.create(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() });
    },
  });
}

/**
 * Hook to delete a project
 */
export function useDeleteProject() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => projectsApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() });
    },
  });
}

/**
 * Hook to trigger project indexing
 */
export function useIndexProject() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, force = false }: { id: string; force?: boolean }) =>
      projectsApi.index(id, force),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: projectKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: projectKeys.status(id) });
    },
  });
}

/**
 * Hook to reindex a project
 */
export function useReindexProject() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, force = false }: { id: string; force?: boolean }) =>
      projectsApi.reindex(id, force),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: projectKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: projectKeys.status(id) });
    },
  });
}
