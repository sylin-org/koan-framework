import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { tagsApi } from '@/api';
import type { TagVocabularyRequestDto } from '@/api';

export const tagVocabularyKeys = {
  all: ['tagVocabulary'] as const,
  list: () => [...tagVocabularyKeys.all, 'list'] as const,
  item: (tag: string) => [...tagVocabularyKeys.all, 'item', tag] as const,
};

export function useTagVocabularyList() {
  return useQuery({
    queryKey: tagVocabularyKeys.list(),
    queryFn: () => tagsApi.list(),
    staleTime: 30_000,
  });
}

export function useCreateTagVocabulary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: TagVocabularyRequestDto) => tagsApi.create(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tagVocabularyKeys.all });
    },
  });
}

export function useUpdateTagVocabulary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ tag, payload }: { tag: string; payload: TagVocabularyRequestDto }) =>
      tagsApi.update(tag, payload),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: tagVocabularyKeys.all });
      queryClient.invalidateQueries({ queryKey: tagVocabularyKeys.item(variables.tag) });
    },
  });
}

export function useDeleteTagVocabulary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (tag: string) => tagsApi.remove(tag),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tagVocabularyKeys.all });
    },
  });
}
