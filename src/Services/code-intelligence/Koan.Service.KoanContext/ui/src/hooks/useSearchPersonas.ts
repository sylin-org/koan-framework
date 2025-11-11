import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { searchPersonasApi } from '@/api';
import type { SearchPersonaRequestDto } from '@/api';

export const searchPersonaKeys = {
  all: ['searchPersonas'] as const,
  list: () => [...searchPersonaKeys.all, 'list'] as const,
  item: (id: string) => [...searchPersonaKeys.all, 'item', id] as const,
};

export function useSearchPersonaList() {
  return useQuery({
    queryKey: searchPersonaKeys.list(),
    queryFn: () => searchPersonasApi.list(),
    staleTime: 30_000,
  });
}

export function useCreateSearchPersona() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: SearchPersonaRequestDto) => searchPersonasApi.create(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: searchPersonaKeys.all });
    },
  });
}

export function useUpdateSearchPersona() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: SearchPersonaRequestDto }) =>
      searchPersonasApi.update(id, payload),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: searchPersonaKeys.all });
      queryClient.invalidateQueries({ queryKey: searchPersonaKeys.item(variables.id) });
    },
  });
}

export function useDeleteSearchPersona() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => searchPersonasApi.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: searchPersonaKeys.all });
    },
  });
}
