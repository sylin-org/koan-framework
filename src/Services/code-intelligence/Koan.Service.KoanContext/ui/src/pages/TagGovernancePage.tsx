import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import { diagnosticsApi } from '@/api';
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
} from 'lucide-react';

const DEFAULT_SAMPLE_COUNT = 5;

export default function TagGovernancePage() {
  const toast = useToast();
  const navigate = useNavigate();

  const [tagFilter, setTagFilter] = useState('');
  const [selectedTag, setSelectedTag] = useState<string | null>(null);
  const [copiedTag, setCopiedTag] = useState<string | null>(null);
  const [sampleProjectId, setSampleProjectId] = useState('');
  const [sampleCount, setSampleCount] = useState(DEFAULT_SAMPLE_COUNT);

  const {
    data: tagDistribution,
    isLoading: tagsLoading,
    isRefetching: tagsRefreshing,
    refetch,
  } = useQuery({
    queryKey: ['tagDistribution'],
    queryFn: () => diagnosticsApi.getTagDistribution(),
  });

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
            <section>
              <div className="flex items-center justify-between mb-4">
                <div>
                  <h2 className="text-lg font-semibold text-foreground">Tag Details</h2>
                  <p className="text-sm text-muted-foreground">
                    {selectedTag
                      ? `Showing insights for “${selectedTag}”.`
                      : 'Select a tag to view insights and actions.'}
                  </p>
                </div>
                {selectedTag && (
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => handleCopy(selectedTag)}
                      className="flex items-center gap-1 px-3 py-2 border border-border rounded-md text-sm hover:bg-muted transition-colors"
                    >
                      {copiedTag === selectedTag ? (
                        <Check className="w-4 h-4 text-success-600" />
                      ) : (
                        <Copy className="w-4 h-4" />
                      )}
                      Copy tag
                    </button>
                    <button
                      onClick={() => handleOpenSearch(selectedTag)}
                      className="flex items-center gap-1 px-3 py-2 bg-primary-600 text-white rounded-md text-sm hover:bg-primary-700 transition-colors"
                    >
                      <Search className="w-4 h-4" />
                      Open search
                    </button>
                  </div>
                )}
              </div>

              {selectedTag ? (
                <div className="grid grid-cols-3 gap-4">
                  <div className="p-4 border border-border rounded-lg bg-card">
                    <p className="text-xs uppercase text-muted-foreground mb-1">Count</p>
                    <p className="text-2xl font-semibold text-foreground">
                      {tagDistribution?.tags.find((t) => t.tag === selectedTag)?.count.toLocaleString() ?? '—'}
                    </p>
                  </div>
                  <div className="p-4 border border-border rounded-lg bg-card">
                    <p className="text-xs uppercase text-muted-foreground mb-1">Coverage</p>
                    <p className="text-2xl font-semibold text-foreground">
                      {(() => {
                        const entry = filteredTags.find((t) => t.tag === selectedTag);
                        return entry ? `${entry.percentage.toFixed(2)}%` : '—';
                      })()}
                    </p>
                  </div>
                  <div className="p-4 border border-border rounded-lg bg-card">
                    <p className="text-xs uppercase text-muted-foreground mb-1">Projects Indexed</p>
                    <p className="text-2xl font-semibold text-foreground">
                      {tagDistribution?.metadata.projectCount ?? '—'}
                    </p>
                  </div>
                </div>
              ) : (
                <div className="p-6 border border-dashed border-border rounded-lg text-center text-muted-foreground">
                  Choose a tag from the sidebar to see its metrics.
                </div>
              )}
            </section>

            <section>
              <div className="flex items-center justify-between mb-4">
                <div>
                  <h2 className="text-lg font-semibold text-foreground">Chunk Samples</h2>
                  <p className="text-sm text-muted-foreground">
                    Request lightweight samples to verify tagging quality or investigate edge cases.
                  </p>
                </div>
                <button
                  onClick={triggerSampleLoad}
                  className="flex items-center gap-2 px-4 py-2 border border-border rounded-lg hover:bg-muted transition-colors"
                  disabled={sampleMutation.isPending}
                >
                  {sampleMutation.isPending ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <Filter className="w-4 h-4" />
                  )}
                  Load samples
                </button>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
                <div>
                  <label className="block text-sm font-medium text-foreground mb-2">Project (optional)</label>
                  <input
                    type="text"
                    value={sampleProjectId}
                    onChange={(e) => setSampleProjectId(e.target.value)}
                    placeholder="project-id"
                    className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-foreground mb-2">Sample size</label>
                  <input
                    type="number"
                    min={1}
                    max={25}
                    value={sampleCount}
                    onChange={(e) => setSampleCount(Number(e.target.value))}
                    className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                  />
                </div>
                <div className="hidden md:block" />
              </div>

              {sampleMutation.isPending && (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Loader2 className="w-4 h-4 animate-spin" />
                  Fetching sample chunks...
                </div>
              )}

              {activeSample && (
                <div className="space-y-4">
                  <p className="text-sm text-muted-foreground">
                    Sampled {activeSample.sampleSize} chunk(s) from project <span className="font-medium text-foreground">{activeSample.projectId}</span>.
                    {selectedTag && (
                      <span>
                        {' '}Showing <span className="font-medium text-foreground">{matchingChunks?.length ?? 0}</span> chunk(s) containing “{selectedTag}”.
                      </span>
                    )}
                  </p>

                  {matchingChunks && matchingChunks.length > 0 ? (
                    <div className="space-y-4">
                      {matchingChunks.map((chunk) => (
                        <div key={chunk.id} className="border border-border rounded-lg p-4 bg-card">
                          <div className="flex items-center justify-between mb-2">
                            <div>
                              <p className="text-sm font-medium text-foreground">{chunk.filePath}</p>
                              <p className="text-xs text-muted-foreground">{chunk.language} • {chunk.tokenCount} tokens</p>
                            </div>
                          </div>
                          <p className="text-sm text-muted-foreground mb-3">{chunk.textPreview}</p>
                          <div className="flex flex-wrap gap-2 text-xs">
                            {chunk.primaryTags.map((tag) => (
                              <span key={`primary-${chunk.id}-${tag}`} className="px-2 py-1 bg-primary-50 text-primary-700 rounded-full">{tag}</span>
                            ))}
                            {chunk.secondaryTags.map((tag) => (
                              <span key={`secondary-${chunk.id}-${tag}`} className="px-2 py-1 bg-muted text-foreground rounded-full">{tag}</span>
                            ))}
                            {chunk.fileTags.map((tag) => (
                              <span key={`file-${chunk.id}-${tag}`} className="px-2 py-1 bg-muted text-muted-foreground rounded-full border border-border">{tag}</span>
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div className="p-6 border border-dashed border-border rounded-lg text-center text-muted-foreground">
                      No matching chunks found in the current sample.
                    </div>
                  )}
                </div>
              )}
            </section>
          </div>
        </main>
      </div>
    </div>
  );
}
