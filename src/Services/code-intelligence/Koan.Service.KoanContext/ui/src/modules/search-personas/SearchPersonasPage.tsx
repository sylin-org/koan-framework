import { useEffect, useMemo, useState } from 'react';
import { Brain, Loader2, Plus, RefreshCcw, Trash2 } from 'lucide-react';
import {
  useSearchPersonaList,
  useCreateSearchPersona,
  useUpdateSearchPersona,
  useDeleteSearchPersona,
} from '@/hooks/useSearchPersonas';
import { useToast } from '@/hooks/useToast';
import type { SearchPersonaDto } from '@/api/types';

interface FormState {
  name: string;
  displayName: string;
  description: string;
  semanticWeight: number;
  tagWeight: number;
  recencyWeight: number;
  maxTokens: number;
  includeInsights: boolean;
  includeReasoning: boolean;
  tagBoosts: string;
  defaultTagsAny: string;
  defaultTagsAll: string;
  defaultTagsExclude: string;
  isActive: boolean;
}

const createEmptyForm = (): FormState => ({
  name: '',
  displayName: '',
  description: '',
  semanticWeight: 0.6,
  tagWeight: 0.3,
  recencyWeight: 0.1,
  maxTokens: 6000,
  includeInsights: true,
  includeReasoning: true,
  tagBoosts: '',
  defaultTagsAny: '',
  defaultTagsAll: '',
  defaultTagsExclude: '',
  isActive: true,
});

const normalizeTags = (value: string) =>
  value
    .split(',')
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);

const formatBoosts = (boosts: Record<string, number>) =>
  Object.entries(boosts)
    .map(([tag, value]) => `${tag}:${value}`)
    .join('\n');

const parseBoosts = (value: string) => {
  const lines = value
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);

  const result: Record<string, number> = {};
  for (const line of lines) {
    const [tag, raw] = line.split(/[:=]/).map((part) => part.trim());
    if (!tag) {
      continue;
    }
    const parsed = Number(raw);
    result[tag.toLowerCase()] = Number.isFinite(parsed) ? Math.min(Math.max(parsed, 0), 1) : 0;
  }

  return result;
};

export default function SearchPersonasPage() {
  const toast = useToast();
  const [filter, setFilter] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [formState, setFormState] = useState<FormState>(() => createEmptyForm());

  const {
    data: personas,
    isLoading,
    isFetching,
    refetch,
  } = useSearchPersonaList();

  const createMutation = useCreateSearchPersona();
  const updateMutation = useUpdateSearchPersona();
  const deleteMutation = useDeleteSearchPersona();

  const sortedPersonas = useMemo(() => {
    if (!personas) {
      return [] as SearchPersonaDto[];
    }

    const items = [...personas].sort((a, b) => a.displayName.localeCompare(b.displayName));

    if (!filter.trim()) {
      return items;
    }

    const query = filter.trim().toLowerCase();
    return items.filter((persona) =>
      persona.displayName.toLowerCase().includes(query) || persona.name.includes(query),
    );
  }, [personas, filter]);

  const selectedPersona = useMemo(() => {
    if (!selectedId || !personas) {
      return undefined;
    }

    return personas.find((persona) => persona.id === selectedId);
  }, [selectedId, personas]);

  useEffect(() => {
    if (!isCreating && selectedPersona) {
      setFormState({
        name: selectedPersona.name,
        displayName: selectedPersona.displayName,
        description: selectedPersona.description,
        semanticWeight: selectedPersona.semanticWeight,
        tagWeight: selectedPersona.tagWeight,
        recencyWeight: selectedPersona.recencyWeight,
        maxTokens: selectedPersona.maxTokens,
        includeInsights: selectedPersona.includeInsights,
        includeReasoning: selectedPersona.includeReasoning,
        tagBoosts: formatBoosts(selectedPersona.tagBoosts ?? {}),
        defaultTagsAny: selectedPersona.defaultTagsAny.join(', '),
        defaultTagsAll: selectedPersona.defaultTagsAll.join(', '),
        defaultTagsExclude: selectedPersona.defaultTagsExclude.join(', '),
        isActive: selectedPersona.isActive,
      });
      return;
    }

    if (isCreating) {
      setFormState(createEmptyForm());
    }
  }, [selectedPersona, isCreating]);

  useEffect(() => {
    if (isLoading || isCreating || selectedId || !sortedPersonas.length) {
      return;
    }

    const [first] = sortedPersonas;
    if (first) {
      setSelectedId(first.id);
    }
  }, [isLoading, isCreating, sortedPersonas, selectedId]);

  const handleCreateNew = () => {
    setIsCreating(true);
    setSelectedId(null);
    setFormState(createEmptyForm());
  };

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const payload = {
      name: formState.name.trim(),
      displayName: formState.displayName.trim() || formState.name.trim(),
      description: formState.description.trim(),
      semanticWeight: Number.isFinite(formState.semanticWeight) ? formState.semanticWeight : 0.6,
      tagWeight: Number.isFinite(formState.tagWeight) ? formState.tagWeight : 0.3,
      recencyWeight: Number.isFinite(formState.recencyWeight) ? formState.recencyWeight : 0.1,
      maxTokens: Number.isFinite(formState.maxTokens) ? Math.round(formState.maxTokens) : 6000,
      includeInsights: formState.includeInsights,
      includeReasoning: formState.includeReasoning,
      tagBoosts: parseBoosts(formState.tagBoosts),
      defaultTagsAny: normalizeTags(formState.defaultTagsAny),
      defaultTagsAll: normalizeTags(formState.defaultTagsAll),
      defaultTagsExclude: normalizeTags(formState.defaultTagsExclude),
      isActive: formState.isActive,
    };

    if (!payload.name) {
      toast.error('Persona identifier is required');
      return;
    }

    if (!payload.displayName) {
      toast.error('Display name is required');
      return;
    }

    if (payload.semanticWeight + payload.tagWeight + payload.recencyWeight > 1.25) {
      toast.warning('Combined weights exceed typical bounds');
    }

    if (isCreating) {
      createMutation.mutate(payload, {
        onSuccess: (result) => {
          toast.success('Persona created', result?.displayName ?? payload.displayName);
          setIsCreating(false);
          setSelectedId(result?.id ?? null);
          refetch();
        },
        onError: (error: any) => {
          toast.error('Failed to create persona', error?.message ?? 'Unknown error');
        },
      });
      return;
    }

    if (!selectedPersona) {
      toast.error('Select a persona to update');
      return;
    }

    updateMutation.mutate(
      { id: selectedPersona.id, payload },
      {
        onSuccess: () => {
          toast.success('Persona updated', payload.displayName);
          refetch();
        },
        onError: (error: any) => {
          toast.error('Failed to update persona', error?.message ?? 'Unknown error');
        },
      },
    );
  };

  const handleDelete = () => {
    if (!selectedPersona) {
      return;
    }

    deleteMutation.mutate(selectedPersona.id, {
      onSuccess: () => {
        toast.success('Persona removed', selectedPersona.displayName);
        setSelectedId(null);
        setIsCreating(true);
        setFormState(createEmptyForm());
        refetch();
      },
      onError: (error: any) => {
        toast.error('Failed to remove persona', error?.message ?? 'Unknown error');
      },
    });
  };

  const isBusy = isFetching || createMutation.isPending || updateMutation.isPending || deleteMutation.isPending;

  return (
    <div className="h-full flex flex-col bg-background">
      <header className="px-6 py-4 border-b border-border bg-card">
        <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold text-foreground">Search Personas</h1>
            <p className="text-sm text-muted-foreground">
              Tune ranking weights, tag boosts, and prompts for tailored search experiences.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => refetch()}
              className="inline-flex items-center gap-2 px-3 py-2 border border-border rounded-lg hover:bg-muted"
              disabled={isFetching}
            >
              {isFetching ? <Loader2 className="w-4 h-4 animate-spin" /> : <RefreshCcw className="w-4 h-4" />}
              Refresh
            </button>
            <button
              onClick={handleCreateNew}
              className="inline-flex items-center gap-2 px-3 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700"
            >
              <Plus className="w-4 h-4" />
              New Persona
            </button>
          </div>
        </div>
      </header>

      <div className="flex-1 flex overflow-hidden">
        <aside className="w-full max-w-sm border-r border-border bg-card flex flex-col">
          <div className="p-4 border-b border-border space-y-3">
            <input
              type="text"
              value={filter}
              onChange={(event) => setFilter(event.target.value)}
              placeholder="Filter personas"
              className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
            <p className="text-xs text-muted-foreground">
              {sortedPersonas.length.toLocaleString()} persona{sortedPersonas.length === 1 ? '' : 's'}
            </p>
          </div>

          {isLoading ? (
            <div className="flex-1 flex items-center justify-center text-muted-foreground">
              <Loader2 className="w-5 h-5 animate-spin" />
            </div>
          ) : sortedPersonas.length === 0 ? (
            <div className="flex-1 flex items-center justify-center text-muted-foreground text-sm px-6 text-center">
              No personas defined yet.{' '}
              <button onClick={handleCreateNew} className="underline">
                Create one
              </button>
              .
            </div>
          ) : (
            <div className="flex-1 overflow-y-auto">
              <ul className="divide-y divide-border">
                {sortedPersonas.map((persona) => {
                  const isActive = !isCreating && persona.id === selectedId;
                  return (
                    <li key={persona.id}>
                      <button
                        type="button"
                        onClick={() => {
                          setSelectedId(persona.id);
                          setIsCreating(false);
                        }}
                        className={`w-full px-4 py-3 flex items-center justify-between text-left transition-colors ${
                          isActive ? 'bg-primary-50 text-primary-700' : 'hover:bg-muted'
                        }`}
                      >
                        <div>
                          <p className="text-sm font-medium leading-tight">{persona.displayName}</p>
                          <p className="text-xs text-muted-foreground">{persona.name}</p>
                        </div>
                        <span className={`text-xs font-medium ${persona.isActive ? 'text-emerald-600' : 'text-muted-foreground'}`}>
                          {persona.isActive ? 'Active' : 'Inactive'}
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            </div>
          )}
        </aside>

        <main className="flex-1 overflow-y-auto">
          <div className="h-full max-w-3xl mx-auto w-full p-6">
            <section className="border border-border rounded-lg bg-card p-6 h-full">
              <header className="flex items-center justify-between mb-6">
                <div>
                  <h2 className="text-lg font-semibold text-foreground">
                    {isCreating
                      ? 'Create Persona'
                      : selectedPersona
                      ? `Edit ${selectedPersona.displayName}`
                      : 'Select a persona'}
                  </h2>
                  <p className="text-sm text-muted-foreground">
                    {isCreating
                      ? 'Capture weightings, boosts, and default tags for a new persona.'
                      : selectedPersona
                      ? 'Adjust ranking weights, toggles, or default tag filters.'
                      : 'Choose a persona from the list to review or edit.'}
                  </p>
                </div>
              </header>

              <form onSubmit={handleSubmit} className="space-y-5">
                <div className="grid gap-4 md:grid-cols-2">
                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Identifier *</span>
                    <input
                      type="text"
                      value={formState.name}
                      onChange={(event) => setFormState((prev) => ({ ...prev, name: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="general"
                      required
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Display name *</span>
                    <input
                      type="text"
                      value={formState.displayName}
                      onChange={(event) => setFormState((prev) => ({ ...prev, displayName: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="General"
                      required
                    />
                  </label>

                  <label className="md:col-span-2 space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Description</span>
                    <input
                      type="text"
                      value={formState.description}
                      onChange={(event) => setFormState((prev) => ({ ...prev, description: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="Balanced persona across docs and APIs"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Semantic weight</span>
                    <input
                      type="number"
                      min={0}
                      max={1}
                      step={0.05}
                      value={formState.semanticWeight}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, semanticWeight: Number(event.target.value) }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Tag weight</span>
                    <input
                      type="number"
                      min={0}
                      max={1}
                      step={0.05}
                      value={formState.tagWeight}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, tagWeight: Number(event.target.value) }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Recency weight</span>
                    <input
                      type="number"
                      min={0}
                      max={1}
                      step={0.05}
                      value={formState.recencyWeight}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, recencyWeight: Number(event.target.value) }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Max tokens</span>
                    <input
                      type="number"
                      min={1000}
                      max={20000}
                      step={500}
                      value={formState.maxTokens}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, maxTokens: Number(event.target.value) }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                  </label>
                </div>

                <div className="grid gap-4 md:grid-cols-2">
                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Default tags (any)</span>
                    <textarea
                      value={formState.defaultTagsAny}
                      onChange={(event) => setFormState((prev) => ({ ...prev, defaultTagsAny: event.target.value }))}
                      className="w-full min-h-[72px] px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="api, docs"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Default tags (all)</span>
                    <textarea
                      value={formState.defaultTagsAll}
                      onChange={(event) => setFormState((prev) => ({ ...prev, defaultTagsAll: event.target.value }))}
                      className="w-full min-h-[72px] px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="adr"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Exclude tags</span>
                    <textarea
                      value={formState.defaultTagsExclude}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, defaultTagsExclude: event.target.value }))
                      }
                      className="w-full min-h-[72px] px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="internal"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Tag boosts (tag:value)</span>
                    <textarea
                      value={formState.tagBoosts}
                      onChange={(event) => setFormState((prev) => ({ ...prev, tagBoosts: event.target.value }))}
                      className="w-full min-h-[96px] px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder={['api:0.4', 'docs:0.2'].join('\n')}
                    />
                    <span className="text-xs text-muted-foreground">
                      One entry per line using <code>tag:value</code> format. Values clamp between 0 and 1.
                    </span>
                  </label>
                </div>

                <div className="flex flex-wrap items-center gap-4">
                  <label className="inline-flex items-center gap-2 text-sm text-foreground">
                    <input
                      type="checkbox"
                      checked={formState.includeInsights}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, includeInsights: event.target.checked }))
                      }
                      className="rounded border-border text-primary-600 focus:ring-primary-500"
                    />
                    Include insights
                  </label>

                  <label className="inline-flex items-center gap-2 text-sm text-foreground">
                    <input
                      type="checkbox"
                      checked={formState.includeReasoning}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, includeReasoning: event.target.checked }))
                      }
                      className="rounded border-border text-primary-600 focus:ring-primary-500"
                    />
                    Include reasoning traces
                  </label>

                  <label className="inline-flex items-center gap-2 text-sm text-foreground">
                    <input
                      type="checkbox"
                      checked={formState.isActive}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, isActive: event.target.checked }))
                      }
                      className="rounded border-border text-primary-600 focus:ring-primary-500"
                    />
                    Activate persona
                  </label>
                </div>

                {!isCreating && selectedPersona && (
                  <div className="rounded-lg border border-dashed border-border bg-background/60 p-4 space-y-3">
                    <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Current values</p>
                    <dl className="grid gap-3 md:grid-cols-2">
                      <div>
                        <dt className="text-xs text-muted-foreground">Identifier</dt>
                        <dd className="text-sm text-foreground break-all">{selectedPersona.id}</dd>
                      </div>
                      <div>
                        <dt className="text-xs text-muted-foreground">Boosts</dt>
                        <dd className="text-sm text-foreground">
                          {Object.keys(selectedPersona.tagBoosts ?? {}).length === 0
                            ? '—'
                            : formatBoosts(selectedPersona.tagBoosts ?? {})}
                        </dd>
                      </div>
                    </dl>
                  </div>
                )}

                <div className="flex flex-wrap items-center gap-3 pt-2">
                  <button
                    type="submit"
                    disabled={isBusy}
                    className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 disabled:opacity-50"
                  >
                    {isBusy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Brain className="w-4 h-4" />}
                    {isCreating ? 'Create persona' : 'Save changes'}
                  </button>

                  {!isCreating && selectedPersona && (
                    <button
                      type="button"
                      onClick={handleDelete}
                      disabled={deleteMutation.isPending}
                      className="inline-flex items-center gap-2 px-4 py-2 border border-border rounded-lg text-destructive hover:bg-destructive/10 disabled:opacity-50"
                    >
                      {deleteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
                      Remove
                    </button>
                  )}
                </div>
              </form>
            </section>
          </div>
        </main>
      </div>
    </div>
  );
}
