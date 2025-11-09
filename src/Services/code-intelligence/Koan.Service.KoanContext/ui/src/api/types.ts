// Auto-generated TypeScript types from C# models

export enum IndexingStatus {
  NotIndexed = 0,
  Indexing = 1,
  Ready = 2,
  Failed = 3,
}

export enum JobStatus {
  Pending = 0,
  Planning = 1,
  Indexing = 2,
  Completed = 3,
  Failed = 4,
  Cancelled = 5,
}

export interface Project {
  id: string;
  name: string;
  rootPath: string;
  docsPath?: string | null;
  lastIndexed?: string | null;
  status: string; // Serialized as string by backend: "NotIndexed" | "Indexing" | "Ready" | "Failed"
  documentCount: number;
  indexedBytes: number;
  commitSha?: string | null;
  lastError?: string | null;
}

export interface Job {
  id: string;
  projectId: string;
  status: string; // Serialized as string by backend: "Pending" | "Planning" | "Indexing" | "Completed" | "Failed" | "Cancelled"
  totalFiles: number;
  processedFiles: number;
  skippedFiles: number;
  errorFiles: number;
  newFiles: number;
  changedFiles: number;
  chunksCreated: number;
  vectorsSaved: number;
  startedAt: string;
  completedAt?: string | null;
  estimatedCompletion?: string | null;
  errorMessage?: string | null;
  currentOperation?: string | null;
  progress: number;
  elapsed: string; // TimeSpan as ISO duration string
}

// Legacy Chunk interface for backward compatibility (used by other parts of the system)
export interface Chunk {
  id: string;
  filePath: string;
  searchText: string;
  commitSha?: string | null;
  startByteOffset: number;
  endByteOffset: number;
  startLine: number;
  endLine: number;
  sourceUrl?: string | null;
  title?: string | null;
  language?: string | null;
  indexedAt: string;
  tokenCount: number;
  category?: string | null;
  pathSegments?: string[] | null;
  fileLastModified: string;
  fileHash?: string | null;
}

// NEW: Search result chunk with nested provenance (matches backend SearchResultChunk)
export interface SearchResultChunk {
  id: string;
  text: string;
  score: number;
  provenance: ChunkProvenance;
  reasoning?: RetrievalReasoning | null;
  projectId?: string; // Present in multi-project searches
}

export interface ChunkProvenance {
  sourceIndex: number;
  startByteOffset: number;
  endByteOffset: number;
  startLine: number;
  endLine: number;
  language?: string | null;
}

export interface RetrievalReasoning {
  semanticScore: number;
  keywordScore: number;
  strategy: string; // "keyword" | "vector" | "hybrid"
}

// API Request/Response types

export interface CreateProjectRequest {
  name: string;
  rootPath: string;
  docsPath?: string | null;
}

export interface IndexMetadataRequest {
  documentCount: number;
  indexedBytes: number;
}

export interface UpdateMonitoringRequest {
  monitorCodeChanges?: boolean | null;
  monitorDocChanges?: boolean | null;
}

export interface BulkIndexRequest {
  projectIds: string[];
  force?: boolean;
}

export interface ProjectStatusResponse {
  projectId: string;
  name: string;
  status: string;
  lastIndexed?: string | null;
  documentCount: number;
  error?: string | null;
  activeJob?: {
    id: string;
    status: string;
    progress: number;
    totalFiles: number;
    processedFiles: number;
    chunksCreated: number;
    vectorsSaved: number;
    startedAt: string;
    estimatedCompletion?: string | null;
    elapsed: string;
    currentOperation?: string | null;
  } | null;
}

export interface ProjectHealthResponse {
  projectId: string;
  name: string;
  healthy: boolean;
  status: string;
  lastIndexed?: string | null;
  documentCount: number;
  indexedBytes: number;
  error?: string | null;
  warnings: string[];
}

export interface SearchRequest {
  query: string;
  projectId?: string | null;
  projectIds?: string[] | null;
  pathContext?: string | null;
  libraryId?: string | null;
  workingDirectory?: string | null;
  alpha?: number | null;
  tokenCounter?: number | null;
  continuationToken?: string | null;
  includeInsights?: boolean | null;
  includeReasoning?: boolean | null;
  languages?: string[] | null;
}

export interface SearchResult {
  projects?: Array<{
    id: string;
    name: string;
  }>;
  chunks: Array<SearchResultChunk>;  // Use new SearchResultChunk type
  metadata?: {
    tokensRequested?: number;
    tokensReturned?: number;
    totalTokens?: number;
    projectCount?: number;
    page?: number;
    model?: string;
    vectorProvider?: string;
    timestamp?: string;
    duration?: string;
  };
  sources?: {
    totalFiles: number;
    files: Array<{
      projectId?: string;
      projectName?: string;
      filePath: string;
      title?: string | null;
      url?: string | null;
      commitSha: string;
    }>;
  };
  insights?: {
    topics: Record<string, number>;
    completenessLevel: string;
    missingTopics?: string[] | null;
  };
  warnings?: string[] | null;
  errors?: string[] | null;
  continuationToken?: string | null;
  reasoning?: string | null;
}

export interface SuggestionRequest {
  prefix: string;
  maxSuggestions?: number | null;
}

export interface SuggestionResponse {
  prefix: string;
  suggestions: string[];
}

export interface JobListResponse {
  projectId: string;
  totalJobs: number;
  jobs: Partial<Job>[];
}

export interface ActiveJobsResponse {
  count: number;
  jobs: Partial<Job>[];
}

export interface AllJobsResponse {
  totalCount: number;
  limit: number;
  offset: number;
  hasMore: boolean;
  jobs: Job[];
}

export interface ListJobsRequest {
  projectId?: string | null;
  status?: string | null; // API expects string: "Pending" | "Planning" | "Indexing" | "Completed" | "Failed" | "Cancelled"
  limit?: number;
  offset?: number;
}

export interface ApiError {
  error: string;
  details?: string;
  hint?: string;
}

export interface AppSettings {
  vectorStore: {
    provider: string;
    host: string;
    dimension: number;
    metric: string;
    defaultTopK: number;
    maxTopK: number;
    timeoutSeconds: number;
  };
  database: {
    provider: string;
    connectionString?: string;
  };
  ai: {
    embedding: {
      provider: string;
      model: string;
      endpoint: string;
    };
  };
  indexing: {
    chunkSize: number;
    maxFileSizeMB: number;
    maxConcurrentJobs: number;
    embeddingBatchSize: number;
    enableParallelProcessing: boolean;
    maxDegreeOfParallelism: number;
    defaultTokenBudget: number;
  };
  fileMonitoring: {
    enabled: boolean;
    debounceMilliseconds: number;
    maxConcurrentReindexOperations: number;
  };
  projectResolution: {
    autoCreate: boolean;
    autoIndex: boolean;
    maxSizeGB: number;
  };
  jobMaintenance: {
    maxJobsPerProject: number;
    jobRetentionDays: number;
    enableAutomaticCleanup: boolean;
  };
  system: {
    baseUrl: string;
    autoResumeIndexing: boolean;
  };
}

export interface ConnectionTestResult {
  success: boolean;
  message: string;
  provider?: string;
  endpoint?: string;
  error?: string;
}

export interface LanguageStats {
  totalChunks: number;
  languages: Array<{
    language: string;
    count: number;
    percentage: number;
  }>;
}
