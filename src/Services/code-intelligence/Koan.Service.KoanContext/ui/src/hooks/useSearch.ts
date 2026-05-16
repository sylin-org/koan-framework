import { useMutation, useQuery } from '@tanstack/react-query';
import { searchApi, type SearchRequest } from '@/api';

/**
 * Hook to perform search
 */
export function useSearch() {
  return useMutation({
    mutationFn: (request: SearchRequest) => searchApi.search(request),
  });
}

/**
 * Hook to get search suggestions
 */
export function useSearchSuggestions() {
  return useMutation({
    mutationFn: (prefix: string) => searchApi.getSuggestions({ prefix }),
  });
}

/**
 * Hook to get available languages for filtering
 */
export function useLanguages(projectId?: string | null, projectIds?: string[] | null) {
  return useQuery({
    queryKey: ['languages', projectId, projectIds],
    queryFn: () => searchApi.getLanguages(projectId, projectIds),
    staleTime: 5 * 60 * 1000, // Cache for 5 minutes
  });
}
