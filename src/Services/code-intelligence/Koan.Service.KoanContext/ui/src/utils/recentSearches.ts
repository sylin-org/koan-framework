/**
 * Recent searches management using localStorage
 */

const RECENT_SEARCHES_KEY = 'koan-context-recent-searches';
const MAX_RECENT_SEARCHES = 5;

export interface RecentSearch {
  query: string;
  timestamp: number;
  projectIds?: string[];
}

/**
 * Get recent searches from localStorage
 */
export function getRecentSearches(): string[] {
  try {
    const stored = localStorage.getItem(RECENT_SEARCHES_KEY);
    if (!stored) return [];

    const searches: RecentSearch[] = JSON.parse(stored);

    // Return just the query strings, sorted by most recent
    return searches
      .sort((a, b) => b.timestamp - a.timestamp)
      .slice(0, MAX_RECENT_SEARCHES)
      .map(s => s.query);
  } catch (error) {
    console.error('Failed to load recent searches:', error);
    return [];
  }
}

/**
 * Add a search to recent searches
 */
export function addRecentSearch(query: string, projectIds?: string[]): void {
  try {
    const stored = localStorage.getItem(RECENT_SEARCHES_KEY);
    let searches: RecentSearch[] = stored ? JSON.parse(stored) : [];

    // Remove existing entry if it exists (to update timestamp)
    searches = searches.filter(s => s.query.toLowerCase() !== query.toLowerCase());

    // Add new search at the beginning
    searches.unshift({
      query,
      timestamp: Date.now(),
      projectIds,
    });

    // Keep only the most recent MAX_RECENT_SEARCHES * 2 (to have some history)
    searches = searches.slice(0, MAX_RECENT_SEARCHES * 2);

    localStorage.setItem(RECENT_SEARCHES_KEY, JSON.stringify(searches));
  } catch (error) {
    console.error('Failed to save recent search:', error);
  }
}

/**
 * Clear all recent searches
 */
export function clearRecentSearches(): void {
  try {
    localStorage.removeItem(RECENT_SEARCHES_KEY);
  } catch (error) {
    console.error('Failed to clear recent searches:', error);
  }
}

/**
 * Get detailed recent search history (with metadata)
 */
export function getRecentSearchHistory(): RecentSearch[] {
  try {
    const stored = localStorage.getItem(RECENT_SEARCHES_KEY);
    if (!stored) return [];

    const searches: RecentSearch[] = JSON.parse(stored);
    return searches.sort((a, b) => b.timestamp - a.timestamp);
  } catch (error) {
    console.error('Failed to load recent search history:', error);
    return [];
  }
}
