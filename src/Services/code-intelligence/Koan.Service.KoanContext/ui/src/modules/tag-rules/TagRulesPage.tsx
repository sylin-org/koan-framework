import { useEffect, useMemo, useState } from 'react';
import { Loader2, Plus, RefreshCcw, ShieldCheck, Trash2 } from 'lucide-react';
import {
  useTagRuleList,
  useCreateTagRule,
  useUpdateTagRule,
  useDeleteTagRule,
} from '@/hooks/useTagRules';
import { useToast } from '@/hooks/useToast';
import type { TagRuleDto } from '@/api/types';

const scopeOptions = [
  { value: 'file', label: 'File' },
  { value: 'chunk', label: 'Chunk' },
  { value: 'frontmatter', label: 'Frontmatter' },
];

const matcherOptions = [
  { value: 'path', label: 'Path Glob' },
  { value: 'extension', label: 'Extension' },
  { value: 'frontmatter', label: 'Frontmatter' },
  { value: 'contentRegex', label: 'Content Regex' },
  { value: 'language', label: 'Language' },
];

interface FormState {
  name: string;
  scope: string;
  matcherType: string;
  pattern: string;
  tags: string;
  confidence: number;
  priority: number;
  isActive: boolean;
}

const createEmptyForm = (): FormState => ({
  name: '',
  scope: 'file',
  matcherType: 'path',
  pattern: '',
  tags: '',
  confidence: 0.8,
  priority: 100,
  isActive: true,
});

const normalizeTags = (value: string) =>
  value
    .split(',')
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);

export default function TagRulesPage() {
  const toast = useToast();
  const [filter, setFilter] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [formState, setFormState] = useState<FormState>(() => createEmptyForm());

  const {
    data: rules,
    isLoading,
    isFetching,
    refetch,
  } = useTagRuleList();

  const createMutation = useCreateTagRule();
  const updateMutation = useUpdateTagRule();
  const deleteMutation = useDeleteTagRule();

  const sortedRules = useMemo(() => {
    if (!rules) {
      return [] as TagRuleDto[];
    }

    const items = [...rules].sort((a, b) => {
      if (a.priority === b.priority) {
        return a.name.localeCompare(b.name);
      }
      return b.priority - a.priority;
    });

    if (!filter.trim()) {
      return items;
    }

    const query = filter.trim().toLowerCase();
    return items.filter((rule) =>
      rule.name.toLowerCase().includes(query) ||
      rule.pattern.toLowerCase().includes(query) ||
      rule.tags.some((tag) => tag.includes(query)),
    );
  }, [rules, filter]);

  const selectedRule = useMemo(() => {
    if (!selectedId || !rules) {
      return undefined;
    }

    return rules.find((rule) => rule.id === selectedId);
  }, [selectedId, rules]);

  useEffect(() => {
    if (!isCreating && selectedRule) {
      setFormState({
        name: selectedRule.name,
        scope: selectedRule.scope,
        matcherType: selectedRule.matcherType,
        pattern: selectedRule.pattern,
        tags: selectedRule.tags.join(', '),
        confidence: selectedRule.confidence,
        priority: selectedRule.priority,
        isActive: selectedRule.isActive,
      });
      return;
    }

    if (isCreating) {
      setFormState(createEmptyForm());
    }
  }, [selectedRule, isCreating]);

  useEffect(() => {
    if (isLoading || isCreating || selectedId || !sortedRules.length) {
      return;
    }

    const [first] = sortedRules;
    if (first) {
      setSelectedId(first.id);
    }
  }, [isLoading, isCreating, sortedRules, selectedId]);

  const handleCreateNew = () => {
    setIsCreating(true);
    setSelectedId(null);
    setFormState(createEmptyForm());
  };

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const payload = {
      name: formState.name.trim(),
      scope: formState.scope,
      matcherType: formState.matcherType,
      pattern: formState.pattern.trim(),
      tags: normalizeTags(formState.tags),
      confidence: Number.isFinite(formState.confidence) ? formState.confidence : 0.8,
      priority: Number.isFinite(formState.priority) ? Math.round(formState.priority) : 100,
      isActive: formState.isActive,
    };

    if (!payload.name) {
      toast.error('Rule name is required');
      return;
    }

    if (!payload.pattern) {
      toast.error('Rule pattern is required');
      return;
    }

    if (payload.tags.length === 0) {
      toast.error('At least one tag must be provided');
      return;
    }

    if (isCreating) {
      createMutation.mutate(payload, {
        onSuccess: (result) => {
          toast.success('Rule created', result?.name ?? payload.name);
          setIsCreating(false);
          setSelectedId(result?.id ?? null);
          refetch();
        },
        onError: (error: any) => {
          toast.error('Failed to create rule', error?.message ?? 'Unknown error');
        },
      });
      return;
    }

    if (!selectedRule) {
      toast.error('Select a rule to update');
      return;
    }

    updateMutation.mutate(
      { id: selectedRule.id, payload },
      {
        onSuccess: () => {
          toast.success('Rule updated', payload.name);
          refetch();
        },
        onError: (error: any) => {
          toast.error('Failed to update rule', error?.message ?? 'Unknown error');
        },
      },
    );
  };

  const handleDelete = () => {
    if (!selectedRule) {
      return;
    }

    deleteMutation.mutate(selectedRule.id, {
      onSuccess: () => {
        toast.success('Rule removed', selectedRule.name);
        setSelectedId(null);
        setIsCreating(true);
        setFormState(createEmptyForm());
        refetch();
      },
      onError: (error: any) => {
        toast.error('Failed to remove rule', error?.message ?? 'Unknown error');
      },
    });
  };

  const isBusy = isFetching || createMutation.isPending || updateMutation.isPending || deleteMutation.isPending;

  return (
    <div className="h-full flex flex-col bg-background">
      <header className="px-6 py-4 border-b border-border bg-card">
        <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold text-foreground">Tag Rules</h1>
            <p className="text-sm text-muted-foreground">
              Configure rule-based tag inference across files, chunks, and frontmatter.
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
              New Rule
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
              placeholder="Filter rules by name, pattern, or tag"
              className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
            <p className="text-xs text-muted-foreground">
              {sortedRules.length.toLocaleString()} rule{sortedRules.length === 1 ? '' : 's'}
            </p>
          </div>

          {isLoading ? (
            <div className="flex-1 flex items-center justify-center text-muted-foreground">
              <Loader2 className="w-5 h-5 animate-spin" />
            </div>
          ) : sortedRules.length === 0 ? (
            <div className="flex-1 flex items-center justify-center text-muted-foreground text-sm px-6 text-center">
              No rules match your filter.{' '}
              <button onClick={handleCreateNew} className="underline">
                Create one
              </button>
              .
            </div>
          ) : (
            <div className="flex-1 overflow-y-auto">
              <ul className="divide-y divide-border">
                {sortedRules.map((rule) => {
                  const isActive = !isCreating && rule.id === selectedId;
                  return (
                    <li key={rule.id}>
                      <button
                        type="button"
                        onClick={() => {
                          setSelectedId(rule.id);
                          setIsCreating(false);
                        }}
                        className={`w-full px-4 py-3 flex items-center justify-between text-left transition-colors ${
                          isActive ? 'bg-primary-50 text-primary-700' : 'hover:bg-muted'
                        }`}
                      >
                        <div>
                          <p className="text-sm font-medium leading-tight">{rule.name}</p>
                          <p className="text-xs text-muted-foreground">{rule.pattern}</p>
                        </div>
                        <span className="text-xs text-muted-foreground flex items-center gap-1">
                          <ShieldCheck className={`w-3 h-3 ${rule.isActive ? 'text-emerald-500' : 'text-muted-foreground'}`} />
                          {rule.priority}
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
                    {isCreating ? 'Create Rule' : selectedRule ? `Edit ${selectedRule.name}` : 'Select a rule'}
                  </h2>
                  <p className="text-sm text-muted-foreground">
                    {isCreating
                      ? 'Define how tags are inferred from naming, content, or metadata.'
                      : selectedRule
                      ? 'Update rule metadata, matching characteristics, or emitted tags.'
                      : 'Choose a rule from the list to review or edit.'}
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
                      placeholder="Docs Path"
                      required
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Scope *</span>
                    <select
                      value={formState.scope}
                      onChange={(event) => setFormState((prev) => ({ ...prev, scope: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    >
                      {scopeOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Matcher *</span>
                    <select
                      value={formState.matcherType}
                      onChange={(event) => setFormState((prev) => ({ ...prev, matcherType: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    >
                      {matcherOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Pattern *</span>
                    <input
                      type="text"
                      value={formState.pattern}
                      onChange={(event) => setFormState((prev) => ({ ...prev, pattern: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="docs/**"
                      required
                    />
                  </label>

                  <label className="md:col-span-2 space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Emitted tags *</span>
                    <textarea
                      value={formState.tags}
                      onChange={(event) => setFormState((prev) => ({ ...prev, tags: event.target.value }))}
                      className="w-full min-h-[96px] px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="docs, adr"
                      required
                    />
                    <span className="text-xs text-muted-foreground">Comma-separated list of tag identifiers.</span>
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Confidence</span>
                    <input
                      type="number"
                      min={0}
                      max={1}
                      step={0.05}
                      value={formState.confidence}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, confidence: Number(event.target.value) }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                  </label>

                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Priority</span>
                    <input
                      type="number"
                      min={0}
                      max={10000}
                      value={formState.priority}
                      onChange={(event) =>
                        setFormState((prev) => ({ ...prev, priority: Number(event.target.value) }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                  </label>
                </div>

                <label className="inline-flex items-center gap-2 text-sm text-foreground">
                  <input
                    type="checkbox"
                    checked={formState.isActive}
                    onChange={(event) =>
                      setFormState((prev) => ({ ...prev, isActive: event.target.checked }))
                    }
                    className="rounded border-border text-primary-600 focus:ring-primary-500"
                  />
                  Activate rule
                </label>

                {!isCreating && selectedRule && (
                  <div className="rounded-lg border border-dashed border-border bg-background/60 p-4 space-y-3">
                    <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Current values</p>
                    <dl className="grid gap-3 md:grid-cols-2">
                      <div>
                        <dt className="text-xs text-muted-foreground">Identifier</dt>
                        <dd className="text-sm text-foreground break-all">{selectedRule.id}</dd>
                      </div>
                      <div>
                        <dt className="text-xs text-muted-foreground">Tags</dt>
                        <dd className="text-sm text-foreground">{selectedRule.tags.join(', ')}</dd>
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
                    {isBusy ? <Loader2 className="w-4 h-4 animate-spin" /> : <ShieldCheck className="w-4 h-4" />}
                    {isCreating ? 'Create rule' : 'Save changes'}
                  </button>

                  {!isCreating && selectedRule && (
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
