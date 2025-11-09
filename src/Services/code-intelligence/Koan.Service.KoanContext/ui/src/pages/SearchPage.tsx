import { useState, useEffect } from 'react';
import { Search as SearchIcon, Filter, X, FileText, ExternalLink, Copy, CheckCircle, Share2 } from 'lucide-react';
import { useSearch, useSearchSuggestions, useLanguages } from '@/hooks/useSearch';
import { useProjects } from '@/hooks/useProjects';
import { useEscapeKey } from '@/hooks/useKeyboardShortcuts';
import { useToast } from '@/hooks/useToast';
import type { SearchResult } from '@/api/types';
import { getRecentSearches, addRecentSearch } from '@/utils/recentSearches';
import { SearchResultSkeleton } from '@/components/Skeleton';

export default function SearchPage() {
  const [query, setQuery] = useState('');
  const [showFilters, setShowFilters] = useState(false);
  const [selectedProjects, setSelectedProjects] = useState<string[]>([]);
  const [selectedLanguages, setSelectedLanguages] = useState<string[]>([]);
  const [alpha, setAlpha] = useState(0.7); // Hybrid search ratio (0=keyword, 1=semantic)
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [dynamicSuggestions, setDynamicSuggestions] = useState<string[]>([]);
  const [allResults, setAllResults] = useState<SearchResult | null>(null);
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [shareUrlCopied, setShareUrlCopied] = useState(false);
  const [recentSearches, setRecentSearches] = useState<string[]>([]);

  const { data: projects } = useProjects();
  const { data: languageStats } = useLanguages(
    selectedProjects.length === 1 ? selectedProjects[0] : null,
    selectedProjects.length > 1 ? selectedProjects : null
  );
  const searchMutation = useSearch();
  const suggestionsMutation = useSearchSuggestions();
  const toast = useToast();

  // Load recent searches from localStorage on mount
  useEffect(() => {
    setRecentSearches(getRecentSearches());
  }, []);

  // Handle Escape key - clear search or close filters
  useEscapeKey(() => {
    if (showFilters) {
      setShowFilters(false);
    } else if (query) {
      setQuery('');
      searchMutation.reset();
    }
  });

  // Fetch suggestions from API with debouncing (300ms delay)
  useEffect(() => {
    if (!query || query.length < 2) {
      setDynamicSuggestions([]);
      return;
    }

    const timeoutId = setTimeout(() => {
      suggestionsMutation.mutate(query, {
        onSuccess: (data) => {
          setDynamicSuggestions(data.suggestions);
        },
        onError: (error) => {
          toast.error('Search Suggestions Failed', error instanceof Error ? error.message : 'Could not load search suggestions');
          setDynamicSuggestions([]);
        },
      });
    }, 300);

    return () => clearTimeout(timeoutId);
  }, [query]);

  // Fallback suggestions when no recent searches available
  const fallbackSuggestions = [
    'authentication middleware',
    'vector provider',
    'entity model',
    'database connection',
    'error handling',
    'API endpoints',
  ];

  // Use dynamic suggestions if available, otherwise use fallback
  const suggestions = dynamicSuggestions.length > 0 ? dynamicSuggestions : fallbackSuggestions;

  // Load search from URL parameters on mount
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const urlQuery = params.get('q');
    const urlProjects = params.get('projects');
    const urlAlpha = params.get('alpha');

    if (urlQuery) {
      setQuery(urlQuery);

      if (urlProjects) {
        setSelectedProjects(urlProjects.split(','));
      }

      if (urlAlpha) {
        setAlpha(parseFloat(urlAlpha));
      }

      // Trigger search automatically
      setTimeout(() => {
        searchMutation.mutate(
          {
            query: urlQuery,
            projectIds: urlProjects ? urlProjects.split(',') : undefined,
            alpha: urlAlpha ? parseFloat(urlAlpha) : 0.7,
            tokenCounter: 5000,
            includeInsights: true,
            includeReasoning: true,
            continuationToken: null,
            languages: selectedLanguages.length > 0 ? selectedLanguages : undefined,
          },
          {
            onSuccess: (data) => {
              setAllResults(data);
              setContinuationToken(data.continuationToken || null);
            },
          }
        );
      }, 100);
    }
  }, []); // Empty deps - only run on mount

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    if (!query.trim()) return;

    // Reset pagination state for new search
    setAllResults(null);
    setContinuationToken(null);
    setIsLoadingMore(false);

    // Perform search across selected projects or all projects
    searchMutation.mutate(
      {
        query,
        projectIds: selectedProjects.length > 0 ? selectedProjects : undefined,
        alpha,
        tokenCounter: 5000,
        includeInsights: true,
        includeReasoning: true,
        continuationToken: null,
        languages: selectedLanguages.length > 0 ? selectedLanguages : undefined,
      },
      {
        onSuccess: (data) => {
          setAllResults(data);
          setContinuationToken(data.continuationToken || null);
          // Save to recent searches
          addRecentSearch(query, selectedProjects.length > 0 ? selectedProjects : undefined);
          setRecentSearches(getRecentSearches()); // Refresh recent searches
        },
      }
    );
  };

  const handleLoadMore = () => {
    if (!continuationToken || !query.trim() || isLoadingMore) return;

    setIsLoadingMore(true);

    searchMutation.mutate(
      {
        query,
        projectIds: selectedProjects.length > 0 ? selectedProjects : undefined,
        alpha,
        tokenCounter: 5000,
        includeInsights: false,
        includeReasoning: false,
        continuationToken,
        languages: selectedLanguages.length > 0 ? selectedLanguages : undefined,
      },
      {
        onSuccess: (data) => {
          // Append new chunks to existing results
          setAllResults((prev) => {
            if (!prev) return data;
            return {
              ...data,
              chunks: [...prev.chunks, ...data.chunks],
              metadata: {
                ...data.metadata,
                totalTokens: (prev.metadata?.totalTokens ?? 0) + (data.metadata?.totalTokens ?? 0),
              },
              insights: prev.insights, // Keep original insights
              reasoning: prev.reasoning, // Keep original reasoning
            };
          });
          setContinuationToken(data.continuationToken || null);
          setIsLoadingMore(false);
        },
        onError: () => {
          setIsLoadingMore(false);
        },
      }
    );
  };

  const handleCopyCode = (chunkId: string, code: string) => {
    navigator.clipboard.writeText(code);
    setCopiedId(chunkId);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const handleShareResults = () => {
    // Build URL with query parameters
    const params = new URLSearchParams();
    params.set('q', query);
    if (selectedProjects.length > 0) {
      params.set('projects', selectedProjects.join(','));
    }
    if (alpha !== 0.7) {
      params.set('alpha', alpha.toString());
    }

    const shareUrl = `${window.location.origin}${window.location.pathname}?${params.toString()}`;
    navigator.clipboard.writeText(shareUrl);
    setShareUrlCopied(true);
    setTimeout(() => setShareUrlCopied(false), 2000);
  };

  // Use accumulated results for display
  const results: SearchResult | null = allResults;

  return (
    <div className="h-full flex flex-col">
      {/* Search Header */}
      <div className="bg-card border-b border-border p-8">
        <div className="max-w-4xl mx-auto">
          <h1 className="text-3xl font-bold text-foreground mb-6">
            Search Your Code Semantically
          </h1>

          {/* Search Bar */}
          <form onSubmit={handleSearch} className="relative">
            <div className="relative">
              <SearchIcon className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-muted-foreground" />
              <input
                type="text"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Search your code... (Press / from anywhere)"
                className="w-full pl-12 pr-24 py-4 text-lg border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                autoFocus
              />
              {query && (
                <button
                  type="button"
                  onClick={() => setQuery('')}
                  className="absolute right-16 top-1/2 -translate-y-1/2 p-1 hover:bg-muted rounded"
                >
                  <X className="w-4 h-4 text-muted-foreground" />
                </button>
              )}
              <button
                type="submit"
                className="absolute right-2 top-1/2 -translate-y-1/2 px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 transition-colors"
              >
                Search
              </button>
            </div>
          </form>

          {/* Recent/Suggested Searches */}
          {!query && recentSearches.length > 0 && (
            <div className="mt-6 flex items-center gap-4 text-sm">
              <span className="text-muted-foreground">Recent:</span>
              <div className="flex flex-wrap gap-2">
                {recentSearches.map((search, i) => (
                  <button
                    key={i}
                    onClick={() => setQuery(search)}
                    className="px-3 py-1 bg-muted text-foreground rounded-full hover:bg-muted/80 transition-colors"
                  >
                    {search}
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* Dynamic Suggestions (shown while typing) */}
          {query && query.length >= 2 && !searchMutation.isPending && (
            <div className="mt-4 flex items-center gap-4 text-sm">
              <span className="text-muted-foreground">
                {suggestionsMutation.isPending ? 'Loading...' : 'Suggestions:'}
              </span>
              {!suggestionsMutation.isPending && suggestions.length > 0 && (
                <div className="flex flex-wrap gap-2">
                  {suggestions.map((suggestion, i) => (
                    <button
                      key={i}
                      onClick={() => setQuery(suggestion)}
                      className="px-3 py-1 bg-primary-50 text-primary-700 rounded-full hover:bg-primary-100 transition-colors"
                    >
                      {suggestion}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Main Content Area */}
      <div className="flex-1 overflow-hidden flex">
        {/* Filters Sidebar (Left) */}
        {showFilters && (
          <aside className="w-64 bg-card border-r border-border p-6 overflow-y-auto">
            <div className="flex items-center justify-between mb-6">
              <h2 className="font-semibold text-foreground">Filters</h2>
              <button
                onClick={() => setShowFilters(false)}
                className="p-1 hover:bg-muted rounded"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Projects Filter */}
            <div className="mb-6">
              <h3 className="text-sm font-medium text-foreground mb-3">Projects</h3>
              <div className="space-y-2">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={selectedProjects.length === 0}
                    onChange={(e) => {
                      if (e.target.checked) {
                        setSelectedProjects([]);
                      }
                    }}
                    className="rounded"
                  />
                  <span>All Projects</span>
                </label>
                {projects?.map((project) => (
                  <label key={project.id} className="flex items-center gap-2 text-sm text-muted-foreground">
                    <input
                      type="checkbox"
                      checked={selectedProjects.includes(project.id)}
                      onChange={(e) => {
                        if (e.target.checked) {
                          setSelectedProjects([...selectedProjects, project.id]);
                        } else {
                          setSelectedProjects(selectedProjects.filter(id => id !== project.id));
                        }
                      }}
                      className="rounded"
                    />
                    <span>{project.name} ({project.documentCount})</span>
                  </label>
                ))}
              </div>
            </div>

            {/* File Types / Languages Filter */}
            <div className="mb-6">
              <h3 className="text-sm font-medium text-foreground mb-3">Languages</h3>
              {languageStats && languageStats.languages.length > 0 ? (
                <div className="space-y-2 max-h-64 overflow-y-auto">
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={selectedLanguages.length === 0}
                      onChange={(e) => {
                        if (e.target.checked) {
                          setSelectedLanguages([]);
                        }
                      }}
                      className="rounded"
                    />
                    <span>All Languages ({languageStats.totalChunks})</span>
                  </label>
                  {languageStats.languages.map((lang) => (
                    <label key={lang.language} className="flex items-center gap-2 text-sm text-muted-foreground">
                      <input
                        type="checkbox"
                        checked={selectedLanguages.includes(lang.language)}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setSelectedLanguages([...selectedLanguages, lang.language]);
                          } else {
                            setSelectedLanguages(selectedLanguages.filter(l => l !== lang.language));
                          }
                        }}
                        className="rounded"
                      />
                      <span>{lang.language} ({lang.count} · {lang.percentage.toFixed(1)}%)</span>
                    </label>
                  ))}
                </div>
              ) : (
                <p className="text-xs text-muted-foreground">No indexed content available</p>
              )}
            </div>

            {/* Relevance Slider */}
            <div className="mb-6">
              <h3 className="text-sm font-medium text-foreground mb-3">
                Min Relevance Score
              </h3>
              <input
                type="range"
                min="0"
                max="100"
                defaultValue="70"
                className="w-full"
              />
              <div className="flex justify-between text-xs text-muted-foreground mt-1">
                <span>0.0</span>
                <span>0.7</span>
                <span>1.0</span>
              </div>
            </div>

            {/* Hybrid Mode Slider */}
            <div className="mb-6">
              <h3 className="text-sm font-medium text-foreground mb-3">
                Hybrid Mode
              </h3>
              <input
                type="range"
                min="0"
                max="100"
                value={alpha * 100}
                onChange={(e) => setAlpha(parseInt(e.target.value) / 100)}
                className="w-full"
              />
              <div className="flex justify-between text-xs text-muted-foreground mt-1">
                <span>Keyword</span>
                <span>{alpha.toFixed(1)}</span>
                <span>Semantic</span>
              </div>
            </div>

            <button
              onClick={() => {
                setSelectedProjects([]);
                setSelectedLanguages([]);
                setAlpha(0.7);
              }}
              className="w-full py-2 text-sm text-primary-600 hover:bg-primary-50 rounded transition-colors"
            >
              Clear All Filters
            </button>
          </aside>
        )}

        {/* Results Area (Right) */}
        <div className="flex-1 overflow-y-auto">
          {!query && !results ? (
            // Empty State
            <div className="max-w-4xl mx-auto p-12 text-center">
              <div className="w-16 h-16 bg-primary-50 rounded-full flex items-center justify-center mx-auto mb-6">
                <SearchIcon className="w-8 h-8 text-primary-600" />
              </div>
              <h2 className="text-2xl font-bold text-foreground mb-3">
                Start Searching Your Code
              </h2>
              <p className="text-muted-foreground mb-8 max-w-md mx-auto">
                Use natural language to find code snippets, functions, and documentation
                across all your indexed projects.
              </p>

              <div className="text-left max-w-md mx-auto bg-card border border-border rounded-lg p-6">
                <h3 className="font-semibold text-foreground mb-4">Try searching for:</h3>
                <ul className="space-y-2 text-sm text-muted-foreground">
                  <li className="flex items-start gap-2">
                    <span className="text-primary-600">•</span>
                    <span>"authentication middleware"</span>
                  </li>
                  <li className="flex items-start gap-2">
                    <span className="text-primary-600">•</span>
                    <span>"database connection setup"</span>
                  </li>
                  <li className="flex items-start gap-2">
                    <span className="text-primary-600">•</span>
                    <span>"error handling patterns"</span>
                  </li>
                </ul>
                <p className="text-xs text-muted-foreground mt-4 italic">
                  Tip: Use natural language, not exact keywords
                </p>
              </div>

              {!showFilters && (
                <button
                  onClick={() => setShowFilters(true)}
                  className="mt-8 inline-flex items-center gap-2 px-4 py-2 text-sm text-primary-600 hover:bg-primary-50 rounded-lg transition-colors"
                >
                  <Filter className="w-4 h-4" />
                  Show Advanced Filters
                </button>
              )}

              {/* Popular Searches */}
              <div className="mt-12">
                <h3 className="text-sm font-medium text-muted-foreground mb-4">
                  Popular Searches
                </h3>
                <div className="flex flex-wrap gap-2 justify-center">
                  {suggestions.map((search, i) => (
                    <button
                      key={i}
                      onClick={() => setQuery(search)}
                      className="px-3 py-1.5 text-sm bg-muted text-foreground rounded-full hover:bg-muted/80 transition-colors"
                    >
                      {search}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          ) : (
            // Results
            <div className="max-w-5xl mx-auto p-8">
              <div className="flex items-center justify-between mb-6">
                <div>
                  <p className="text-sm text-muted-foreground">
                    {searchMutation.isPending ? (
                      'Searching...'
                    ) : results ? (
                      <>
                        Found <span className="font-medium text-foreground">{results.chunks.length}</span> results for "
                        <span className="font-medium text-foreground">{query}</span>"
                      </>
                    ) : (
                      `Searching for "${query}"`
                    )}
                  </p>
                  {results && results.insights && (
                    <p className="text-xs text-primary-600 mt-1">
                      💡 Completeness: {results.insights.completenessLevel} • Topics: {Object.keys(results.insights.topics).length}
                    </p>
                  )}
                </div>
                <div className="flex items-center gap-2">
                  {results && results.chunks.length > 0 && (
                    <button
                      onClick={handleShareResults}
                      className="inline-flex items-center gap-2 px-3 py-1.5 text-sm text-primary-600 hover:bg-primary-50 rounded-lg transition-colors"
                      title="Share search results"
                    >
                      {shareUrlCopied ? (
                        <>
                          <CheckCircle className="w-4 h-4" />
                          Copied!
                        </>
                      ) : (
                        <>
                          <Share2 className="w-4 h-4" />
                          Share
                        </>
                      )}
                    </button>
                  )}
                  {!showFilters && (
                    <button
                      onClick={() => setShowFilters(true)}
                      className="inline-flex items-center gap-2 px-3 py-1.5 text-sm text-primary-600 hover:bg-primary-50 rounded-lg transition-colors"
                    >
                      <Filter className="w-4 h-4" />
                      Filters
                    </button>
                  )}
                </div>
              </div>

              {/* Loading State */}
              {searchMutation.isPending && (
                <div className="space-y-4">
                  {[1, 2, 3, 4].map((i) => (
                    <SearchResultSkeleton key={i} />
                  ))}
                </div>
              )}

              {/* Error State */}
              {searchMutation.isError && (
                <div className="bg-danger-50 border border-danger-200 rounded-lg p-6 text-center">
                  <p className="text-danger-900 font-medium">Search failed</p>
                  <p className="text-sm text-danger-700 mt-1">
                    {searchMutation.error instanceof Error ? searchMutation.error.message : 'Unknown error occurred'}
                  </p>
                  <button
                    onClick={() => searchMutation.reset()}
                    className="mt-3 px-4 py-2 text-sm bg-danger-600 text-white rounded-lg hover:bg-danger-700 transition-colors"
                  >
                    Try Again
                  </button>
                </div>
              )}

              {/* Results List */}
              {results && results.chunks.length > 0 && (
                <>
                  <div className="space-y-4">
                    {results.chunks.map((chunk) => (
                      <div key={chunk.id} className="bg-card border border-border rounded-lg p-5 hover:border-primary-300 transition-colors">
                        {/* Header */}
                        <div className="flex items-start justify-between mb-3">
                          <div className="flex items-start gap-3 flex-1">
                            <FileText className="w-5 h-5 text-primary-600 mt-0.5" />
                            <div className="flex-1">
                              <div className="flex items-center gap-2">
                                <h3 className="font-medium text-foreground">{chunk.title || chunk.filePath}</h3>
                                {chunk.language && (
                                  <span className="px-2 py-0.5 text-xs bg-muted text-muted-foreground rounded">
                                    {chunk.language}
                                  </span>
                                )}
                              </div>
                              <p className="text-sm text-muted-foreground mt-1">{chunk.filePath}</p>
                              <p className="text-xs text-muted-foreground mt-0.5">
                                Lines {chunk.startLine}-{chunk.endLine} · {chunk.tokenCount} tokens
                              </p>
                            </div>
                          </div>
                          <div className="flex items-center gap-2">
                            <button
                              onClick={() => handleCopyCode(chunk.id, chunk.searchText)}
                              className="p-2 hover:bg-muted rounded transition-colors"
                              title="Copy code"
                            >
                              {copiedId === chunk.id ? (
                                <CheckCircle className="w-4 h-4 text-success-600" />
                              ) : (
                                <Copy className="w-4 h-4 text-muted-foreground" />
                              )}
                            </button>
                            {chunk.sourceUrl && (
                              <a
                                href={chunk.sourceUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="p-2 hover:bg-muted rounded transition-colors"
                                title="Open in editor"
                              >
                                <ExternalLink className="w-4 h-4 text-muted-foreground" />
                              </a>
                            )}
                          </div>
                        </div>

                        {/* Code Preview */}
                        <div className="bg-muted/30 rounded-lg p-4 font-mono text-sm overflow-x-auto">
                          <pre className="whitespace-pre-wrap break-words">{chunk.searchText}</pre>
                        </div>
                      </div>
                    ))}
                  </div>

                  {/* Load More Button */}
                  {continuationToken && (
                    <div className="mt-6 text-center">
                      <button
                        onClick={handleLoadMore}
                        disabled={isLoadingMore}
                        className="px-6 py-3 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed inline-flex items-center gap-2"
                      >
                        {isLoadingMore ? (
                          <>
                            <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin"></div>
                            Loading more results...
                          </>
                        ) : (
                          <>Load More Results</>
                        )}
                      </button>
                      <p className="text-xs text-muted-foreground mt-2">
                        Showing {results.chunks.length} results · {(results.metadata?.totalTokens ?? 0).toLocaleString()} tokens
                      </p>
                    </div>
                  )}

                  {/* End of Results Indicator */}
                  {!continuationToken && results.chunks.length > 0 && (
                    <div className="mt-6 text-center">
                      <p className="text-sm text-muted-foreground">
                        End of results · {results.chunks.length} total · {(results.metadata?.totalTokens ?? 0).toLocaleString()} tokens
                      </p>
                    </div>
                  )}
                </>
              )}

              {/* No Results */}
              {results && results.chunks.length === 0 && !searchMutation.isPending && (
                <div className="text-center py-12">
                  <SearchIcon className="w-12 h-12 text-muted-foreground mx-auto mb-3" />
                  <p className="text-lg font-medium text-foreground mb-2">No results found</p>
                  <p className="text-sm text-muted-foreground mb-6">
                    Try adjusting your search query or filters
                  </p>
                  <div className="flex flex-wrap gap-2 justify-center">
                    {suggestions.map((search, i) => (
                      <button
                        key={i}
                        onClick={() => setQuery(search)}
                        className="px-3 py-1.5 text-sm bg-muted text-foreground rounded-full hover:bg-muted/80 transition-colors"
                      >
                        {search}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
