import { useEffect, useMemo, useState } from 'react';
import {
  Loader2,
  Plus,
  RefreshCcw,
  Search,
  Tag as TagIcon,
  Trash2,
} from 'lucide-react';
import {
  useTagVocabularyList,
  useCreateTagVocabulary,
  useUpdateTagVocabulary,
  useDeleteTagVocabulary,
} from '@/hooks/useTags';
import { useToast } from '@/hooks/useToast';
import type { TagVocabularyEntryDto } from '@/api/types';

interface FormState {
  tag: string;
  displayName: string;
  synonyms: string;
  isPrimary: boolean;
}

const createEmptyForm = (): FormState => ({
  tag: '',
  displayName: '',
  synonyms: '',
  isPrimary: true,
});

const normalizeSynonyms = (value: string) =>
  value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);

export default function TagsPage() {
  const toast = useToast();
  const [filter, setFilter] = useState('');
  const [selectedTag, setSelectedTag] = useState<string | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [formState, setFormState] = useState<FormState>(() => createEmptyForm());

  const {
    data: vocabulary,
    isLoading,
    isFetching,
    refetch,
  } = useTagVocabularyList();

  const createMutation = useCreateTagVocabulary();
  const updateMutation = useUpdateTagVocabulary();
  const deleteMutation = useDeleteTagVocabulary();

  const sortedVocabulary = useMemo(() => {
    if (!vocabulary) {
      return [];
    }

    const items = [...vocabulary].sort((a, b) => a.tag.localeCompare(b.tag));
    if (!filter.trim()) {
      return items;
    }

    const query = filter.trim().toLowerCase();
    return items.filter((entry) => {
      const matchesTag = entry.tag.includes(query);
      const matchesDisplay = entry.displayName?.toLowerCase().includes(query) ?? false;
      const matchesSynonyms = entry.synonyms.some((syn) => syn.includes(query));
      return matchesTag || matchesDisplay || matchesSynonyms;
    });
  }, [vocabulary, filter]);

  const selectedEntry = useMemo<TagVocabularyEntryDto | undefined>(() => {
    if (!selectedTag || !vocabulary) {
      return undefined;
    }

    return vocabulary.find((entry) => entry.tag === selectedTag);
  }, [selectedTag, vocabulary]);

  useEffect(() => {
    if (!isCreating && selectedEntry) {
      setFormState({
        tag: selectedEntry.tag,
        displayName: selectedEntry.displayName ?? '',
        synonyms: selectedEntry.synonyms.join(', '),
        isPrimary: selectedEntry.isPrimary,
      });
      return;
    }

    if (isCreating) {
      setFormState(createEmptyForm());
    }
  }, [selectedEntry, isCreating]);

  useEffect(() => {
    if (isLoading || isCreating || selectedTag || !vocabulary?.length) {
      return;
    }

    const [first] = vocabulary;
    if (first) {
      setSelectedTag(first.tag);
    }
  }, [isLoading, isCreating, selectedTag, vocabulary]);

  const handleSelectTag = (tag: string) => {
    setSelectedTag(tag);
    setIsCreating(false);
  };

  const handleCreateNew = () => {
    setIsCreating(true);
    setSelectedTag(null);
    setFormState(createEmptyForm());
  };

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const normalizedTag = formState.tag.trim();
    if (!normalizedTag) {
      toast.error('Tag name is required');
      return;
    }

    const payload = {
      tag: normalizedTag,
      displayName: formState.displayName.trim() || undefined,
      synonyms: normalizeSynonyms(formState.synonyms),
      isPrimary: formState.isPrimary,
    };

    if (isCreating) {
      createMutation.mutate(payload, {
        onSuccess: (result) => {
          const createdTag = result?.tag ?? payload.tag;
          toast.success('Tag created', createdTag);
          setIsCreating(false);
          setSelectedTag(createdTag);
          refetch();
        },
        onError: (error: any) => {
          toast.error('Failed to create tag', error?.message ?? 'Unknown error');
        },
      });
      return;
    }

    if (!selectedEntry) {
      toast.error('Select a tag to update');
      return;
    }

    updateMutation.mutate(
      { tag: selectedEntry.tag, payload },
      {
        onSuccess: () => {
          toast.success('Tag updated', payload.tag);
          setSelectedTag(payload.tag);
          refetch();
        },
        onError: (error: any) => {
          toast.error('Failed to update tag', error?.message ?? 'Unknown error');
        },
      },
    );
  };

  const handleDelete = () => {
    if (!selectedEntry) {
      return;
    }

    deleteMutation.mutate(selectedEntry.tag, {
      onSuccess: () => {
        toast.success('Tag removed', selectedEntry.tag);
        setSelectedTag(null);
        setIsCreating(true);
        setFormState(createEmptyForm());
        refetch();
      },
      onError: (error: any) => {
        toast.error('Failed to remove tag', error?.message ?? 'Unknown error');
      },
    });
  };

  const isBusy = isFetching || createMutation.isPending || updateMutation.isPending || deleteMutation.isPending;

  return (
    <div className="h-full flex flex-col bg-background">
      <header className="px-6 py-4 border-b border-border bg-card">
        <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold text-foreground">Tags</h1>
            <p className="text-sm text-muted-foreground">
              Manage canonical tags and their synonyms for downstream experiences.
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
              New Tag
            </button>
          </div>
        </div>
      </header>

      <div className="flex-1 flex overflow-hidden">
        <aside className="w-full max-w-sm border-r border-border bg-card flex flex-col">
          <div className="p-4 border-b border-border space-y-3">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <input
                type="text"
                value={filter}
                onChange={(event) => setFilter(event.target.value)}
                placeholder="Filter tags by name or synonym"
                className="w-full pl-9 pr-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
            </div>
            <p className="text-xs text-muted-foreground">
              {sortedVocabulary.length.toLocaleString()} tag{sortedVocabulary.length === 1 ? '' : 's'}
            </p>
          </div>

          {isLoading ? (
            <div className="flex-1 flex items-center justify-center text-muted-foreground">
              <Loader2 className="w-5 h-5 animate-spin" />
            </div>
          ) : sortedVocabulary.length === 0 ? (
            <div className="flex-1 flex items-center justify-center text-muted-foreground text-sm px-6 text-center">
              No tags match your filter.{' '}
              <button onClick={handleCreateNew} className="underline">
                Create one
              </button>
              .
            </div>
          ) : (
            <div className="flex-1 overflow-y-auto">
              <ul className="divide-y divide-border">
                {sortedVocabulary.map((entry) => {
                  const isActive = !isCreating && selectedTag === entry.tag;
                  return (
                    <li key={entry.id}>
                      <button
                        type="button"
                        onClick={() => handleSelectTag(entry.tag)}
                        className={`w-full px-4 py-3 flex items-center justify-between text-left transition-colors ${
                          isActive ? 'bg-primary-50 text-primary-700' : 'hover:bg-muted'
                        }`}
                      >
                        <div className="flex items-center gap-3">
                          <TagIcon className="w-4 h-4" />
                          <div>
                            <p className="text-sm font-medium leading-tight">{entry.tag}</p>
                            {entry.displayName && (
                              <p className="text-xs text-muted-foreground">{entry.displayName}</p>
                            )}
                          </div>
                        </div>
                        <span className="text-xs text-muted-foreground">
                          {entry.synonyms.length} synonym{entry.synonyms.length === 1 ? '' : 's'}
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
                    {isCreating ? 'Create Tag' : selectedEntry ? `Edit ${selectedEntry.tag}` : 'Select a tag'}
                  </h2>
                  <p className="text-sm text-muted-foreground">
                    {isCreating
                      ? 'Define a canonical tag and optional synonyms.'
                      : selectedEntry
                        ? 'Update tag metadata or manage synonyms.'
                        : 'Choose a tag from the list to view details.'}
                  </p>
                </div>
              </header>

              <form onSubmit={handleSubmit} className="space-y-5">
                <div className="grid gap-4 sm:grid-cols-2">
                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Tag *</span>
                    <input
                      type="text"
                      value={formState.tag}
                      onChange={(event) => setFormState((prev) => ({ ...prev, tag: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="docs"
                      required
                    />
                  </label>
                  <label className="space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Display name</span>
                    <input
                      type="text"
                      value={formState.displayName}
                      onChange={(event) => setFormState((prev) => ({ ...prev, displayName: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="Documentation"
                    />
                  </label>
                  <label className="sm:col-span-2 space-y-1 text-sm text-foreground">
                    <span className="text-xs font-medium text-muted-foreground">Synonyms</span>
                    <textarea
                      value={formState.synonyms}
                      onChange={(event) => setFormState((prev) => ({ ...prev, synonyms: event.target.value }))}
                      className="w-full min-h-[96px] px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="doc, documentation, knowledge-base"
                    />
                    <span className="text-xs text-muted-foreground">Comma-separated list of alternate tokens.</span>
                  </label>
                </div>

                <label className="inline-flex items-center gap-2 text-sm text-foreground">
                  <input
                    type="checkbox"
                    checked={formState.isPrimary}
                    onChange={(event) => setFormState((prev) => ({ ...prev, isPrimary: event.target.checked }))}
                    className="rounded border-border text-primary-600 focus:ring-primary-500"
                  />
                  Mark as primary tag
                </label>

                {!isCreating && selectedEntry && (
                  <div className="rounded-lg border border-dashed border-border bg-background/60 p-4 space-y-3">
                    <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Current values</p>
                    <dl className="grid gap-2 sm:grid-cols-2">
                      <div>
                        <dt className="text-xs text-muted-foreground">Identifier</dt>
                        <dd className="text-sm text-foreground break-all">{selectedEntry.id}</dd>
                      </div>
                      <div>
                        <dt className="text-xs text-muted-foreground">Synonyms</dt>
                        <dd className="text-sm text-foreground">
                          {selectedEntry.synonyms.length === 0 ? (
                            <span className="text-muted-foreground">None</span>
                          ) : (
                            <span className="flex flex-wrap gap-2">
                              {selectedEntry.synonyms.map((synonym) => (
                                <span
                                  key={synonym}
                                  className="inline-flex items-center px-2 py-0.5 rounded-full bg-primary-50 text-xs text-primary-700"
                                >
                                  {synonym}
                                </span>
                              ))}
                            </span>
                          )}
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
                    {isBusy ? <Loader2 className="w-4 h-4 animate-spin" /> : <TagIcon className="w-4 h-4" />}
                    {isCreating ? 'Create tag' : 'Save changes'}
                  </button>
                  {!isCreating && selectedEntry && (
                    <button
                      type="button"
                      onClick={handleDelete}
                      disabled={deleteMutation.isPending}
                      className="inline-flex items-center gap-2 px-4 py-2 border border-border rounded-lg text-destructive hover:bg-destructive/10 disabled:opacity-50"
                    >
                      {deleteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
                      Delete
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
