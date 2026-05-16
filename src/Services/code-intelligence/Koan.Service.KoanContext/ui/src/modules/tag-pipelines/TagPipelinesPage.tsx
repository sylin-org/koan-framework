import { useEffect, useMemo, useState } from 'react';
import { ChevronRight, Loader2, Plus, RefreshCcw, ToggleLeft, Trash2 } from 'lucide-react';
import {
  useTagPipelineList,
  useCreateTagPipeline,
  useUpdateTagPipeline,
  useDeleteTagPipeline,
} from '@/hooks/useTagPipelines';
import { useTagRuleList } from '@/hooks/useTagRules';
import { useToast } from '@/hooks/useToast';
import type { TagPipelineDto } from '@/api/types';

interface FormState {
  name: string;
  description: string;
  ruleIds: string[];
  maxPrimaryTags: number;
  maxSecondaryTags: number;
  enableAiFallback: boolean;
}

const createEmptyForm = (): FormState => ({
  name: '',
  description: '',
  ruleIds: [],
  maxPrimaryTags: 6,
  maxSecondaryTags: 10,
  enableAiFallback: false,
});

export default function TagPipelinesPage() {
  const toast = useToast();
  const [filter, setFilter] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [formState, setFormState] = useState<FormState>(() => createEmptyForm());

  const {
    data: pipelines,
    isLoading,
    isFetching,
    refetch,
  } = useTagPipelineList();

  const { data: rules } = useTagRuleList();

  const createMutation = useCreateTagPipeline();
  const updateMutation = useUpdateTagPipeline();
  const deleteMutation = useDeleteTagPipeline();

  const sortedPipelines = useMemo(() => {
    if (!pipelines) {
      return [] as TagPipelineDto[];
    }

    const items = [...pipelines].sort((a, b) => a.name.localeCompare(b.name));

    if (!filter.trim()) {
      return items;
    }

    const query = filter.trim().toLowerCase();
    return items.filter((pipeline) =>
      pipeline.name.includes(query) || pipeline.description.toLowerCase().includes(query),
    );
  }, [pipelines, filter]);

  const selectedPipeline = useMemo(() => {
    if (!selectedId || !pipelines) {
      return undefined;
    }

    return pipelines.find((pipeline) => pipeline.id === selectedId);
  }, [selectedId, pipelines]);

  useEffect(() => {
    if (!isCreating && selectedPipeline) {
      setFormState({
        name: selectedPipeline.name,
        description: selectedPipeline.description,
        ruleIds: selectedPipeline.ruleIds ?? [],
        maxPrimaryTags: selectedPipeline.maxPrimaryTags,
        maxSecondaryTags: selectedPipeline.maxSecondaryTags,
        enableAiFallback: selectedPipeline.enableAiFallback,
      });
      return;
    }

    if (isCreating) {
      setFormState(createEmptyForm());
    }
  }, [selectedPipeline, isCreating]);

  useEffect(() => {
    if (isLoading || isCreating || selectedId || !sortedPipelines.length) {
      return;
    }

    const [first] = sortedPipelines;
    if (first) {
      setSelectedId(first.id);
    }
  }, [isLoading, isCreating, sortedPipelines, selectedId]);

  const handleCreateNew = () => {
    setIsCreating(true);
    setSelectedId(null);
    setFormState(createEmptyForm());
  };

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const payload = {
      name: formState.name.trim().toLowerCase(),
      description: formState.description.trim(),
      ruleIds: [...new Set(formState.ruleIds)],
      maxPrimaryTags: Number.isFinite(formState.maxPrimaryTags) ? formState.maxPrimaryTags : 6,
      maxSecondaryTags: Number.isFinite(formState.maxSecondaryTags) ? formState.maxSecondaryTags : 10,
      enableAiFallback: formState.enableAiFallback,
    };

    if (!payload.name) {
      toast.error('Pipeline name is required');
      return;
    }

    if (payload.ruleIds.length === 0) {
      toast.warning('Pipeline has no rules assigned');
    }

    if (isCreating) {
      createMutation.mutate(payload, {
        onSuccess: (result) => {
          toast.success('Pipeline created', result?.name ?? payload.name);
          setIsCreating(false);
          setSelectedId(result?.id ?? null);
          refetch();
        },
        onError: (error: any) => {
          toast.error('Failed to create pipeline', error?.message ?? 'Unknown error');
        },
      });
      return;
    }

    if (!selectedPipeline) {
      toast.error('Select a pipeline to update');
      return;
    }

    updateMutation.mutate(
      { id: selectedPipeline.id, payload },
      {
        onSuccess: () => {
          toast.success('Pipeline updated', payload.name);
          refetch();
        },
        onError: (error: any) => {
          toast.error('Failed to update pipeline', error?.message ?? 'Unknown error');
        },
      },
    );
  };

  const handleDelete = () => {
    if (!selectedPipeline) {
      return;
    }

    deleteMutation.mutate(selectedPipeline.id, {
      onSuccess: () => {
        toast.success('Pipeline removed', selectedPipeline.name);
        setSelectedId(null);
        setIsCreating(true);
        setFormState(createEmptyForm());
        refetch();
      },
      onError: (error: any) => {
        toast.error('Failed to remove pipeline', error?.message ?? 'Unknown error');
      },
    });
  };

  const isBusy = isFetching || createMutation.isPending || updateMutation.isPending || deleteMutation.isPending;

  return (
    <div className="h-full flex flex-col bg-background">
      <header className="px-6 py-4 border-b border-border bg-card">
        <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold text-foreground">Tag Pipelines</h1>
            <p className="text-sm text-muted-foreground">
              Coordinate rule execution, tag limits, and AI fallbacks for indexing runs.
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
              New Pipeline
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
              placeholder="Filter pipelines"
              className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
            <p className="text-xs text-muted-foreground">
              {sortedPipelines.length.toLocaleString()} pipeline{sortedPipelines.length === 1 ? '' : 's'}
            </p>
          </div>

          {isLoading ? (
            <div className="flex-1 flex items-center justify-center text-muted-foreground">
              <Loader2 className="w-5 h-5 animate-spin" />
            </div>
          ) : sortedPipelines.length === 0 ? (
            <div className="flex-1 flex items-center justify-center text-muted-foreground text-sm px-6 text-center">
              No pipelines found.{' '}
              <button onClick={handleCreateNew} className="underline">
                Create one
              </button>
              .
            </div>
          ) : (
            <div className="flex-1 overflow-y-auto">
              <ul className="divide-y divide-border">
                {sortedPipelines.map((pipeline) => {
                  const isActive = !isCreating && pipeline.id === selectedId;
                  return (
                    <li key={pipeline.id}>
                      <button
                        type="button"
                        onClick={() => {
                          setSelectedId(pipeline.id);
                          setIsCreating(false);
                        }}
                        className={`w-full px-4 py-3 flex items-center justify-between text-left transition-colors ${
                          isActive ? 'bg-primary-50 text-primary-700' : 'hover:bg-muted'
                        }`}
                      >
                        <div>
                          <p className="text-sm font-medium leading-tight">{pipeline.name}</p>
                          <p className="text-xs text-muted-foreground">
                            {pipeline.ruleIds.length.toLocaleString()} rule{pipeline.ruleIds.length === 1 ? '' : 's'}
                          </p>
                        </div>
                        <ChevronRight className="w-4 h-4 text-muted-foreground" />
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
                      ? 'Create Pipeline'
                      : selectedPipeline
                      ? `Edit ${selectedPipeline.name}`
                      : 'Select a pipeline'}
                  </h2>
                  <p className="text-sm text-muted-foreground">
                    {isCreating
                      ? 'Bundle related rules and configure AI fallbacks.'
                      : selectedPipeline
                      ? 'Update rule assignments or adjust tag limits.'
                      : 'Choose a pipeline from the list to review or edit.'}
                  </p>
                </div>
              </header>

              <form onSubmit={handleSubmit} className="space-y-5">
                <div className="grid gap-4 md:grid-cols-2">
                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Name *</span>
                    <input
                      type="text"
                      value={formState.name}
                      onChange={(event) => setFormState((prev) => ({ ...prev, name: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="default"
                      required
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Description</span>
                    <input
                      type="text"
                      value={formState.description}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, description: event.target.value }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="Default pipeline for Koan indexing"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Max primary tags</span>
                    <input
                      type="number"
                      min={1}
                      max={50}
                      value={formState.maxPrimaryTags}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, maxPrimaryTags: Number(event.target.value) }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Max secondary tags</span>
                    <input
                      type="number"
                      min={0}
                      max={200}
                      value={formState.maxSecondaryTags}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, maxSecondaryTags: Number(event.target.value) }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                  </label>
                </div>

                <fieldset className="border border-dashed border-border rounded-lg p-4 space-y-3">
                  <legend className="text-xs font-semibold text-muted-foreground uppercase tracking-wide px-2">
                    Rules
                  </legend>
                  <p className="text-xs text-muted-foreground">
                    Assign one or more rules in the order they should execute. Ordering is derived from rule priority.
                  </p>
                  <div className="grid gap-2 sm:grid-cols-2">
                    {rules?.map((rule) => {
                      const checked = formState.ruleIds.includes(rule.id);
                      return (
                        <label key={rule.id} className="flex items-center gap-2 text-sm">
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={(event) => {
                              setFormState((prev) => {
                                const next = new Set(prev.ruleIds);
                                if (event.target.checked) {
                                  next.add(rule.id);
                                } else {
                                  next.delete(rule.id);
                                }
                                return { ...prev, ruleIds: Array.from(next) };
                              });
                            }}
                            className="rounded border-border text-primary-600 focus:ring-primary-500"
                          />
                          <span className="flex-1">
                            <span className="block text-foreground">{rule.name}</span>
                            <span className="block text-[11px] text-muted-foreground">{rule.pattern}</span>
                          </span>
                        </label>
                      );
                    }) ?? (
                      <p className="text-xs text-muted-foreground col-span-2">No rules available yet.</p>
                    )}
                  </div>
                </fieldset>

                <label className="inline-flex items-center gap-2 text-sm text-foreground">
                  <input
                    type="checkbox"
                    checked={formState.enableAiFallback}
                    onChange={(event) =>
                      setFormState((prev) => ({ ...prev, enableAiFallback: event.target.checked }))
                    }
                    className="rounded border-border text-primary-600 focus:ring-primary-500"
                  />
                  Enable AI fallback
                </label>

                {!isCreating && selectedPipeline && (
                  <div className="rounded-lg border border-dashed border-border bg-background/60 p-4 space-y-3">
                    <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Current values</p>
                    <dl className="grid gap-3 md:grid-cols-2">
                      <div>
                        <dt className="text-xs text-muted-foreground">Identifier</dt>
                        <dd className="text-sm text-foreground break-all">{selectedPipeline.id}</dd>
                      </div>
                      <div>
                        <dt className="text-xs text-muted-foreground">Rules</dt>
                        <dd className="text-sm text-foreground">{selectedPipeline.ruleIds.join(', ') || '—'}</dd>
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
                    {isBusy ? <Loader2 className="w-4 h-4 animate-spin" /> : <ToggleLeft className="w-4 h-4" />}
                    {isCreating ? 'Create pipeline' : 'Save changes'}
                  </button>

                  {!isCreating && selectedPipeline && (
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
