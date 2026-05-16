import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { tagRulesApi } from '@/api';
import type { TagRuleRequestDto } from '@/api';

export const tagRuleKeys = {
  all: ['tagRules'] as const,
  list: () => [...tagRuleKeys.all, 'list'] as const,
  item: (id: string) => [...tagRuleKeys.all, 'item', id] as const,
};

export function useTagRuleList() {
  return useQuery({
    queryKey: tagRuleKeys.list(),
    queryFn: () => tagRulesApi.list(),
    staleTime: 30_000,
  });
}

export function useCreateTagRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: TagRuleRequestDto) => tagRulesApi.create(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tagRuleKeys.all });
    },
  });
}

export function useUpdateTagRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: TagRuleRequestDto }) => tagRulesApi.update(id, payload),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: tagRuleKeys.all });
      queryClient.invalidateQueries({ queryKey: tagRuleKeys.item(variables.id) });
    },
  });
}

export function useDeleteTagRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => tagRulesApi.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tagRuleKeys.all });
    },
  });
}
