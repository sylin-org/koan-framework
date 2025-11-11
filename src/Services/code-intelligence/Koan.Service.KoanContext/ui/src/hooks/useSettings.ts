import { useQuery, useMutation } from '@tanstack/react-query';
import { settingsApi } from '@/api';
import type { TagSeedSummaryDto } from '@/api';

/**
 * Query keys for settings
 */
export const settingsKeys = {
  all: ['settings'] as const,
  detail: () => [...settingsKeys.all, 'detail'] as const,
};

/**
 * Hook to fetch application settings
 */
export function useSettings() {
  return useQuery({
    queryKey: settingsKeys.detail(),
    queryFn: () => settingsApi.get(),
    staleTime: 60000, // Settings don't change often, cache for 1 minute
  });
}

/**
 * Hook to test vector store connection
 */
export function useTestVectorStore() {
  return useMutation({
    mutationFn: () => settingsApi.testVectorStore(),
  });
}

/**
 * Hook to test database connection
 */
export function useTestDatabase() {
  return useMutation({
    mutationFn: () => settingsApi.testDatabase(),
  });
}

/**
 * Hook to force seed tag catalog
 */
export function useSeedTags() {
  return useMutation<TagSeedSummaryDto>({
    mutationFn: () => settingsApi.seedTags(),
  });
}
