import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import { diagnosticsApi } from '@/api';
import {
  useTagVocabularyList,
  useCreateTagVocabulary,
  useUpdateTagVocabulary,
  useDeleteTagVocabulary,
} from '@/hooks/useTags';
import { useToast } from '@/hooks/useToast';
import type { ChunkSample } from '@/api/types';
import {
  Loader2,
  RefreshCcw,
  Tag as TagIcon,
  Copy,
  Check,
  Search,
  Filter,
  Trash2,
} from 'lucide-react';

const DEFAULT_SAMPLE_COUNT = 5;

type TagFormState = {
  tag: string;
  displayName: string;
  synonyms: string;
  isPrimary: boolean;
};

const createEmptyFormState = (): TagFormState => ({
  tag: '',
  displayName: '',
  synonyms: '',
  isPrimary: true,
});

export default function TagGovernancePage() {
  const toast = useToast();
  const navigate = useNavigate();

  const [tagFilter, setTagFilter] = useState('');
  const [selectedTag, setSelectedTag] = useState<string | null>(null);
  const [copiedTag, setCopiedTag] = useState<string | null>(null);
  const [sampleProjectId, setSampleProjectId] = useState('');
  const [sampleCount, setSampleCount] = useState(DEFAULT_SAMPLE_COUNT);
  const [createTagInput, setCreateTagInput] = useState<TagFormState>(() => createEmptyFormState());
  const [editTagInput, setEditTagInput] = useState<TagFormState>(() => createEmptyFormState());

  const {
    data: tagDistribution,
    isLoading: tagsLoading,
    isRefetching: tagsRefreshing,
    refetch,
  } = useQuery({
    queryKey: ['tagDistribution'],
    queryFn: () => diagnosticsApi.getTagDistribution(),
  });

  const {
    data: tagVocabulary,
    isLoading: vocabularyLoading,
    refetch: refetchVocabulary,
  } = useTagVocabularyList();

  const createTagMutation = useCreateTagVocabulary();
  const updateTagMutation = useUpdateTagVocabulary();
  const deleteTagMutation = useDeleteTagVocabulary();

  const sampleMutation = useMutation({
    mutationFn: diagnosticsApi.getChunkSample,
    onError: (error: any) => {
      toast.error('Failed to load sample', error?.error ?? 'Diagnostics endpoint returned an error.');
    },
  });

  const totalChunks = tagDistribution?.totalChunks ?? 0;

  const filteredTags = useMemo(() => {
    if (!tagDistribution) {
      return [];
    }

    const query = tagFilter.trim().toLowerCase();
    const entries = query.length === 0
      ? tagDistribution.tags
      : tagDistribution.tags.filter((entry) => entry.tag.toLowerCase().includes(query));

    return entries.map((entry) => ({
      ...entry,
      percentage: totalChunks === 0 ? 0 : (entry.count / totalChunks) * 100,
    }));
  }, [tagDistribution, tagFilter, totalChunks]);

  const activeSample = sampleMutation.data;
  const selectedVocabularyEntry = useMemo(() => {
    if (!selectedTag || !tagVocabulary) {
      return undefined;
    }

    const normalized = selectedTag.toLowerCase();
    return tagVocabulary.find((entry) => entry.tag === normalized);
  }, [selectedTag, tagVocabulary]);

  useEffect(() => {
    if (selectedVocabularyEntry) {
      setEditTagInput({
        tag: selectedVocabularyEntry.tag,
        displayName: selectedVocabularyEntry.displayName ?? '',
        synonyms: selectedVocabularyEntry.synonyms.join(', '),
        isPrimary: selectedVocabularyEntry.isPrimary,
      });
      return;
    }

    setEditTagInput(createEmptyFormState());
  }, [selectedVocabularyEntry]);
  const matchingChunks: ChunkSample[] | undefined = useMemo(() => {
    if (!activeSample) {
      return undefined;
    }

    if (!selectedTag) {
      return activeSample.chunks;
    }

    const targetTag = selectedTag.toLowerCase();
    return activeSample.chunks.filter((chunk) =>
      chunk.primaryTags.some((tag) => tag.toLowerCase() === targetTag) ||
      chunk.secondaryTags.some((tag) => tag.toLowerCase() === targetTag) ||
      chunk.fileTags.some((tag) => tag.toLowerCase() === targetTag));
  }, [activeSample, selectedTag]);

  const handleCopy = (tag: string) => {
    navigator.clipboard.writeText(tag);
    setCopiedTag(tag);
    setTimeout(() => setCopiedTag(null), 2000);
  };

  const handleOpenSearch = (tag: string) => {
    const params = new URLSearchParams();
    params.set('tagsAny', tag);
    navigate({ pathname: '/', search: `?${params.toString()}` });
  };

  const parseSynonyms = (value: string) =>
    value
      .split(',')
      .map((synonym) => synonym.trim())
      .filter(Boolean);

  const handleCreateTag = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const tag = createTagInput.tag.trim();
    if (!tag) {
      toast.error('Tag is required');
      return;
    }

    const payload = {
      tag,
      displayName: createTagInput.displayName.trim() || undefined,
      synonyms: parseSynonyms(createTagInput.synonyms),
      isPrimary: createTagInput.isPrimary,
    };

    createTagMutation.mutate(payload, {
      onSuccess: () => {
        toast.success('Tag created', `Created ${payload.tag}`);
        setCreateTagInput(createEmptyFormState());
        setSelectedTag(payload.tag);
        refetchVocabulary();
      },
      onError: (error: any) => {
        toast.error('Failed to create tag', error?.message ?? 'Unknown error');
      },
    });
  };

  const handleUpdateTag = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedVocabularyEntry) {
      return;
    }

    const payload = {
      tag: editTagInput.tag.trim(),
      displayName: editTagInput.displayName.trim() || undefined,
      synonyms: parseSynonyms(editTagInput.synonyms),
      isPrimary: editTagInput.isPrimary,
    };

    updateTagMutation.mutate(
      { tag: selectedVocabularyEntry.tag, payload },
      {
        onSuccess: () => {
          toast.success('Tag updated', `Updated ${payload.tag}`);
          setSelectedTag(payload.tag);
          refetchVocabulary();
        },
        onError: (error: any) => {
          toast.error('Failed to update tag', error?.message ?? 'Unknown error');
        },
      },
    );
  };

  const handleDeleteTag = () => {
    if (!selectedVocabularyEntry) {
      return;
    }

    deleteTagMutation.mutate(selectedVocabularyEntry.tag, {
      onSuccess: () => {
        toast.success('Tag deleted', `Removed ${selectedVocabularyEntry.tag}`);
        setSelectedTag(null);
        refetchVocabulary();
      },
      onError: (error: any) => {
        toast.error('Failed to delete tag', error?.message ?? 'Unknown error');
      },
    });
  };

  const triggerSampleLoad = () => {
    sampleMutation.mutate({
      projectId: sampleProjectId.trim() || undefined,
      count: sampleCount,
    });
  };

  return (
    <div className="h-full flex flex-col bg-background">
      <div className="px-6 py-4 border-b border-border bg-card">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-bold text-foreground">Tag Governance</h1>
            <p className="text-sm text-muted-foreground mt-1">
              Inspect tag coverage, spot gaps, and launch targeted searches.
            </p>
          </div>
          <button
            onClick={() => refetch()}
            className="flex items-center gap-2 px-4 py-2 border border-border rounded-lg hover:bg-muted transition-colors"
            disabled={tagsLoading || tagsRefreshing}
          >
            {tagsLoading || tagsRefreshing ? (
              <Loader2 className="w-4 h-4 animate-spin" />
            ) : (
              <RefreshCcw className="w-4 h-4" />
            )}
            Refresh
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-hidden flex">
        <aside className="w-1/3 border-r border-border overflow-y-auto bg-card">
          <div className="p-6 space-y-6">
            <div>
              <label className="block text-sm font-medium text-foreground mb-2">Filter Tags</label>
              <div className="relative">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                <input
                  type="text"
                  value={tagFilter}
                  onChange={(e) => setTagFilter(e.target.value)}
                  placeholder="Type to filter tags"
                  className="w-full pl-10 pr-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                />
              </div>
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <h2 className="text-sm font-medium text-foreground">Tag Distribution</h2>
                <span className="text-xs text-muted-foreground">{filteredTags.length} tags</span>
              </div>

              {tagsLoading ? (
                <div className="flex items-center justify-center py-20 text-muted-foreground">
                  <Loader2 className="w-5 h-5 animate-spin" />
                </div>
              ) : filteredTags.length === 0 ? (
                <p className="text-sm text-muted-foreground">No tags match the current filter.</p>
              ) : (
                <div className="space-y-2">
                  {filteredTags.map((entry) => {
                    const isActive = selectedTag === entry.tag;
                    return (
                      <button
                        key={entry.tag}
                        onClick={() => setSelectedTag(entry.tag)}
                        className={`w-full text-left px-3 py-2 rounded-lg border transition-colors ${
                          isActive ? 'border-primary-600 bg-primary-50 text-primary-700' : 'border-border hover:border-primary-300'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-2">
                            <TagIcon className="w-4 h-4" />
                            <span className="font-medium truncate">{entry.tag}</span>
                          </div>
                          <div className="text-right">
                            <p className="text-sm font-semibold">{entry.count.toLocaleString()}</p>
                            <p className="text-xs text-muted-foreground">{entry.percentage.toFixed(2)}%</p>
                          </div>
                        </div>
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          </div>
        </aside>

        <main className="flex-1 overflow-y-auto">
          <div className="max-w-4xl mx-auto py-8 px-8 space-y-8">
            <section className="border border-border rounded-lg bg-card p-6">
              <div className="flex items-center justify-between mb-4">
                <div>
                  <h2 className="text-lg font-semibold text-foreground">Tag Vocabulary</h2>
                  <p className="text-sm text-muted-foreground">
                    Create, edit, or remove canonical tags that drive pipeline resolution.
                  </p>
                </div>
                {vocabularyLoading && <Loader2 className="w-4 h-4 animate-spin text-muted-foreground" />}
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                <form onSubmit={handleCreateTag} className="space-y-4 border border-border rounded-lg p-4 bg-background">
                  <h3 className="text-sm font-semibold text-foreground">Create Tag</h3>
                  <div className="space-y-2">
                    <label className="block text-xs font-medium text-muted-foreground">Tag *</label>
                    <input
                      type="text"
                      value={createTagInput.tag}
                      onChange={(event) => setCreateTagInput((prev) => ({ ...prev, tag: event.target.value }))}
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="docs"
                      required
                    />
                  </div>
                  <div className="space-y-2">
                    <label className="block text-xs font-medium text-muted-foreground">Display Name</label>
                    <input
                      type="text"
                      value={createTagInput.displayName}
                      onChange={(event) =>
                        setCreateTagInput((prev) => ({ ...prev, displayName: event.target.value }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="Documentation"
                    />
                  </div>
                  <div className="space-y-2">
                    <label className="block text-xs font-medium text-muted-foreground">Synonyms</label>
                    <input
                      type="text"
                      value={createTagInput.synonyms}
                      onChange={(event) =>
                        setCreateTagInput((prev) => ({ ...prev, synonyms: event.target.value }))
                      }
                      className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                      placeholder="doc, documentation"
                    />
                    <p className="text-xs text-muted-foreground">Comma-separated list of synonyms.</p>
                  </div>
                  <label className="inline-flex items-center gap-2 text-sm text-foreground">
                    <input
                      type="checkbox"
                      checked={createTagInput.isPrimary}
                      onChange={(event) =>
                        setCreateTagInput((prev) => ({ ...prev, isPrimary: event.target.checked }))
                      }
                      className="rounded border-border text-primary-600 focus:ring-primary-500"
                    />
                    Mark as primary tag
                  </label>
                  <button
                    type="submit"
                    disabled={createTagMutation.isPending}
                    className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50"
                  >
                    {createTagMutation.isPending ? (
                      <Loader2 className="w-4 h-4 animate-spin" />
                    ) : (
                      <TagIcon className="w-4 h-4" />
                    )}
                    Create
                  </button>
                </form>

                <div className="space-y-4 border border-border rounded-lg p-4 bg-background">
                  <h3 className="text-sm font-semibold text-foreground">Edit Tag</h3>
                  {selectedVocabularyEntry ? (
                    <form onSubmit={handleUpdateTag} className="space-y-4">
                      <div className="space-y-2">
                        <label className="block text-xs font-medium text-muted-foreground">Tag *</label>
                        <input
                          type="text"
                          value={editTagInput.tag}
                          onChange={(event) =>
                            setEditTagInput((prev) => ({ ...prev, tag: event.target.value }))
                          }
                          className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                          required
                        />
                      </div>
                      <div className="space-y-2">
                        <label className="block text-xs font-medium text-muted-foreground">Display Name</label>
                        <input
                          type="text"
                          value={editTagInput.displayName}
                          onChange={(event) =>
                            setEditTagInput((prev) => ({ ...prev, displayName: event.target.value }))
                          }
                          className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                        />
                      </div>
                      <div className="space-y-2">
                        <label className="block text-xs font-medium text-muted-foreground">Synonyms</label>
                        <input
                          type="text"
                          value={editTagInput.synonyms}
                          onChange={(event) =>
                            setEditTagInput((prev) => ({ ...prev, synonyms: event.target.value }))
                          }
                          className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                        />
                        <p className="text-xs text-muted-foreground">Comma-separated list of synonyms.</p>
                      </div>
                      <label className="inline-flex items-center gap-2 text-sm text-foreground">
                        <input
                          type="checkbox"
                          checked={editTagInput.isPrimary}
                          onChange={(event) =>
                            setEditTagInput((prev) => ({ ...prev, isPrimary: event.target.checked }))
                          }
                          className="rounded border-border text-primary-600 focus:ring-primary-500"
                        />
                        Mark as primary tag
                      </label>
                      <div className="flex items-center gap-3">
                        <button
                          type="submit"
                          disabled={updateTagMutation.isPending}
                          className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50"
                        >
                          {updateTagMutation.isPending ? (
                            <Loader2 className="w-4 h-4 animate-spin" />
                          ) : (
                            <TagIcon className="w-4 h-4" />
                          )}
                          Save Changes
                        </button>
                        <button
                          type="button"
                          onClick={handleDeleteTag}
                          className="inline-flex items-center gap-2 px-4 py-2 border border-border rounded-lg text-destructive hover:bg-destructive/10"
                          disabled={deleteTagMutation.isPending}
                        >
                          {deleteTagMutation.isPending ? (
                            <Loader2 className="w-4 h-4 animate-spin" />
                          ) : (
                            <Trash2 className="w-4 h-4" />
                          )}
                          Delete
                        </button>
                      </div>
                    </form>
                  ) : (
                    <div className="text-sm text-muted-foreground">
                      Select a tag from the list to edit its properties.
                    </div>
                  )}
                </div>
              </div>
            </section>

            <section className="border border-border rounded-lg bg-card p-6">
              <div className="flex items-center justify-between mb-4">
                <div>
                  <h2 className="text-lg font-semibold text-foreground">Diagnostics</h2>
                  <p className="text-sm text-muted-foreground">
                    Sample chunks that currently emit the selected tag.
                  </p>
                </div>
                <button
                  onClick={triggerSampleLoad}
                  className="inline-flex items-center gap-2 px-3 py-2 border border-border rounded-lg hover:bg-muted"
                  disabled={sampleMutation.isPending}
                >
                  {sampleMutation.isPending ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <Filter className="w-4 h-4" />
                  )}
                  Run Sample
                </button>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
                <label className="space-y-2 text-sm text-foreground">
                  <span className="text-xs font-medium text-muted-foreground">Project ID (optional)</span>
                  <input
                    type="text"
                    value={sampleProjectId}
                    onChange={(event) => setSampleProjectId(event.target.value)}
                    className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    placeholder="project-123"
                  />
                </label>
                <label className="space-y-2 text-sm text-foreground">
                  <span className="text-xs font-medium text-muted-foreground">Sample Size</span>
                  <input
                    type="number"
                    value={sampleCount}
                    onChange={(event) => setSampleCount(Number(event.target.value) || DEFAULT_SAMPLE_COUNT)}
                    className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                    min={1}
                    max={50}
                  />
                </label>
                <div className="space-y-2 text-sm text-foreground">
                  <span className="text-xs font-medium text-muted-foreground">Selected Tag</span>
                  <div className="px-3 py-2 bg-muted border border-border rounded-lg text-foreground">
                    {selectedTag ?? 'All tags'}
                  </div>
                </div>
              </div>

              {sampleMutation.isPending ? (
                <div className="flex items-center justify-center py-16 text-muted-foreground">
                  <Loader2 className="w-6 h-6 animate-spin" />
                  <span className="ml-3">Loading sample...</span>
                </div>
              ) : !matchingChunks || matchingChunks.length === 0 ? (
                <div className="text-sm text-muted-foreground">
                  No sample available. Run diagnostics or adjust filters.
                </div>
              ) : (
                <div className="space-y-4">
                  {matchingChunks.map((chunk) => (
                    <article key={chunk.id} className="border border-border rounded-lg p-4 bg-background space-y-3">
                      <header className="flex items-start justify-between gap-4">
                        <div>
                          <h3 className="text-sm font-semibold text-foreground">{chunk.title ?? chunk.filePath}</h3>
                          <p className="text-xs text-muted-foreground">{chunk.filePath}</p>
                        </div>
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => handleOpenSearch(chunk.primaryTags[0] ?? selectedTag ?? chunk.fileTags[0] ?? '')}
                            className="inline-flex items-center gap-2 px-3 py-1.5 border border-border rounded-lg text-sm hover:bg-muted"
                          >
                            <Search className="w-3 h-3" />
                            Search
                          </button>
                          <button
                            onClick={() => handleCopy(chunk.id)}
                            className="inline-flex items-center gap-2 px-3 py-1.5 border border-border rounded-lg text-sm hover:bg-muted"
                          >
                            {copiedTag === chunk.id ? (
                              <Check className="w-3 h-3 text-primary-600" />
                            ) : (
                              <Copy className="w-3 h-3" />
                            )}
                            Copy ID
                          </button>
                        </div>
                      </header>

                      <div className="flex flex-wrap gap-2 text-xs">
                        {chunk.primaryTags.map((tag) => (
                          <span key={`primary-${tag}`} className="px-2 py-1 bg-primary-100 text-primary-700 rounded-md">
                            {tag}
                          </span>
                        ))}
                        {chunk.secondaryTags.map((tag) => (
                          <span key={`secondary-${tag}`} className="px-2 py-1 bg-muted rounded-md">
                            {tag}
                          </span>
                        ))}
                        {chunk.fileTags.map((tag) => (
                          <span key={`file-${tag}`} className="px-2 py-1 bg-muted rounded-md">
                            {tag}
                          </span>
                        ))}
                      </div>

                      <pre className="bg-muted p-3 rounded-lg text-xs text-foreground whitespace-pre-wrap max-h-48 overflow-y-auto">
                        {chunk.textPreview}
                      </pre>
                    </article>
                  ))}
                </div>
              )}
            </section>
          </div>
        </main>
      </div>
    </div>
  );
}
