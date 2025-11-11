import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { tagPipelinesApi } from '@/api';
import type { TagPipelineRequestDto } from '@/api';

export const tagPipelineKeys = {
  all: ['tagPipelines'] as const,
  list: () => [...tagPipelineKeys.all, 'list'] as const,
  item: (id: string) => [...tagPipelineKeys.all, 'item', id] as const,
};

export function useTagPipelineList() {
  return useQuery({
    queryKey: tagPipelineKeys.list(),
    queryFn: () => tagPipelinesApi.list(),
    staleTime: 30_000,
  });
}

export function useCreateTagPipeline() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: TagPipelineRequestDto) => tagPipelinesApi.create(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tagPipelineKeys.all });
    },
  });
}

export function useUpdateTagPipeline() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: TagPipelineRequestDto }) =>
      tagPipelinesApi.update(id, payload),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: tagPipelineKeys.all });
      queryClient.invalidateQueries({ queryKey: tagPipelineKeys.item(variables.id) });
    },
  });
}

export function useDeleteTagPipeline() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => tagPipelinesApi.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tagPipelineKeys.all });
    },
  });
}
