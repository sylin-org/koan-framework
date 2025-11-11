import { useState } from 'react';
import { useSettings, useTestVectorStore, useTestDatabase, useSeedTags } from '@/hooks/useSettings';
import {
  Settings as SettingsIcon,
  Database,
  Cpu,
  FileSearch,
  Zap,
  Check,
  X,
  Loader2,
  AlertCircle,
  Info,
  Sparkles,
} from 'lucide-react';

export default function SettingsPage() {
  const { data: settings, isLoading, error } = useSettings();
  const testVectorStore = useTestVectorStore();
  const testDatabase = useTestDatabase();
  const seedTags = useSeedTags();

  const [activeTab, setActiveTab] = useState<'vectorStore' | 'database' | 'ai' | 'indexing' | 'advanced'>('vectorStore');

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background p-8 flex items-center justify-center">
        <Loader2 className="w-8 h-8 text-primary-600 animate-spin" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="min-h-screen bg-background p-8">
        <div className="bg-danger-50 border border-danger-200 rounded-lg p-6 max-w-2xl mx-auto">
          <p className="text-danger-900 font-medium">Failed to load settings</p>
          <p className="text-sm text-danger-700 mt-1">
            {error instanceof Error ? error.message : 'Unknown error occurred'}
          </p>
        </div>
      </div>
    );
  }

  if (!settings) return null;

  const formatSeedTimestamp = (value: string) => {
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
  };

  const tabs = [
    { id: 'vectorStore' as const, label: 'Vector Store', icon: Database },
    { id: 'database' as const, label: 'Database', icon: Database },
    { id: 'ai' as const, label: 'AI Models', icon: Cpu },
    { id: 'indexing' as const, label: 'Indexing', icon: FileSearch },
    { id: 'advanced' as const, label: 'Advanced', icon: Zap },
  ];

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-7xl mx-auto p-8">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-3xl font-bold text-foreground flex items-center gap-3">
            <SettingsIcon className="w-8 h-8" />
            Settings
          </h1>
          <p className="text-muted-foreground mt-2">
            Configure application settings and connections (read-only for now)
          </p>
        </div>

        {/* Info Banner */}
        <div className="mb-6 bg-primary-50 border border-primary-200 rounded-lg p-4">
          <div className="flex items-start gap-3">
            <Info className="w-5 h-5 text-primary-600 mt-0.5" />
            <div>
              <p className="text-sm font-medium text-primary-900">
                Configuration Settings
              </p>
              <p className="text-sm text-primary-700 mt-1">
                These settings are currently read-only and configured via appsettings.json.
                Future versions will allow runtime updates. Click "Test Connection" buttons to verify connectivity.
              </p>
            </div>
          </div>
        </div>

        <div className="grid grid-cols-12 gap-6">
          {/* Tabs Sidebar */}
          <div className="col-span-3">
            <div className="bg-card border border-border rounded-lg p-2">
              {tabs.map((tab) => {
                const Icon = tab.icon;
                const isActive = activeTab === tab.id;
                return (
                  <button
                    key={tab.id}
                    onClick={() => setActiveTab(tab.id)}
                    className={`w-full flex items-center gap-3 px-4 py-3 rounded-lg transition-colors ${
                      isActive
                        ? 'bg-primary-100 text-primary-900'
                        : 'hover:bg-muted text-foreground'
                    }`}
                  >
                    <Icon className="w-5 h-5" />
                    <span className="font-medium">{tab.label}</span>
                  </button>
                );
              })}
            </div>
          </div>

          {/* Settings Content */}
          <div className="col-span-9">
            <div className="bg-card border border-border rounded-lg">
              {/* Vector Store Tab */}
              {activeTab === 'vectorStore' && (
                <div className="p-6">
                  <div className="flex items-center justify-between mb-6">
                    <div>
                      <h2 className="text-xl font-bold text-foreground">Vector Store Configuration</h2>
                      <p className="text-sm text-muted-foreground mt-1">
                        Weaviate vector database settings
                      </p>
                    </div>
                    <button
                      onClick={() => testVectorStore.mutate()}
                      disabled={testVectorStore.isPending}
                      className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50"
                    >
                      {testVectorStore.isPending ? (
                        <Loader2 className="w-4 h-4 animate-spin" />
                      ) : (
                        <Database className="w-4 h-4" />
                      )}
                      Test Connection
                    </button>
                  </div>

                  {testVectorStore.data && (
                    <div
                      className={`mb-4 p-4 rounded-lg border ${
                        testVectorStore.data.success
                          ? 'bg-success-50 border-success-200'
                          : 'bg-danger-50 border-danger-200'
                      }`}
                    >
                      <div className="flex items-start gap-2">
                        {testVectorStore.data.success ? (
                          <Check className="w-5 h-5 text-success-600 mt-0.5" />
                        ) : (
                          <X className="w-5 h-5 text-danger-600 mt-0.5" />
                        )}
                        <div>
                          <p
                            className={`font-medium ${
                              testVectorStore.data.success ? 'text-success-900' : 'text-danger-900'
                            }`}
                          >
                            {testVectorStore.data.message}
                          </p>
                          {testVectorStore.data.error && (
                            <p className="text-sm text-danger-700 mt-1">{testVectorStore.data.error}</p>
                          )}
                        </div>
                      </div>
                    </div>
                  )}

                  <div className="space-y-4">
                    <SettingField label="Provider" value={settings.vectorStore.provider} />
                    <SettingField label="Host Port" value={settings.vectorStore.host} />
                    <SettingField label="Vector Dimension" value={settings.vectorStore.dimension} />
                    <SettingField label="Distance Metric" value={settings.vectorStore.metric} />
                    <SettingField label="Default Top K" value={settings.vectorStore.defaultTopK} />
                    <SettingField label="Max Top K" value={settings.vectorStore.maxTopK} />
                    <SettingField label="Timeout (seconds)" value={settings.vectorStore.timeoutSeconds} />
                  </div>
                </div>
              )}

              {/* Database Tab */}
              {activeTab === 'database' && (
                <div className="p-6">
                  <div className="flex items-center justify-between mb-6">
                    <div>
                      <h2 className="text-xl font-bold text-foreground">Database Configuration</h2>
                      <p className="text-sm text-muted-foreground mt-1">
                        SQL database connection settings
                      </p>
                    </div>
                    <button
                      onClick={() => testDatabase.mutate()}
                      disabled={testDatabase.isPending}
                      className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50"
                    >
                      {testDatabase.isPending ? (
                        <Loader2 className="w-4 h-4 animate-spin" />
                      ) : (
                        <Database className="w-4 h-4" />
                      )}
                      Test Connection
                    </button>
                  </div>

                  {testDatabase.data && (
                    <div
                      className={`mb-4 p-4 rounded-lg border ${
                        testDatabase.data.success
                          ? 'bg-success-50 border-success-200'
                          : 'bg-danger-50 border-danger-200'
                      }`}
                    >
                      <div className="flex items-start gap-2">
                        {testDatabase.data.success ? (
                          <Check className="w-5 h-5 text-success-600 mt-0.5" />
                        ) : (
                          <X className="w-5 h-5 text-danger-600 mt-0.5" />
                        )}
                        <div>
                          <p
                            className={`font-medium ${
                              testDatabase.data.success ? 'text-success-900' : 'text-danger-900'
                            }`}
                          >
                            {testDatabase.data.message}
                          </p>
                          {testDatabase.data.error && (
                            <p className="text-sm text-danger-700 mt-1">{testDatabase.data.error}</p>
                          )}
                        </div>
                      </div>
                    </div>
                  )}

                  <div className="space-y-4">
                    <SettingField label="Provider" value={settings.database.provider} />
                    <SettingField
                      label="Connection String"
                      value={settings.database.connectionString || 'Not configured'}
                      monospace
                    />
                  </div>
                </div>
              )}

              {/* AI Models Tab */}
              {activeTab === 'ai' && (
                <div className="p-6">
                  <h2 className="text-xl font-bold text-foreground mb-2">AI Model Configuration</h2>
                  <p className="text-sm text-muted-foreground mb-6">
                    Embedding and chat model settings
                  </p>

                  <div className="space-y-6">
                    <div>
                      <h3 className="text-lg font-semibold text-foreground mb-4">Embedding Model</h3>
                      <div className="space-y-4">
                        <SettingField label="Provider" value={settings.ai.embedding.provider} />
                        <SettingField label="Model" value={settings.ai.embedding.model} />
                        <SettingField label="Endpoint" value={settings.ai.embedding.endpoint} monospace />
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* Indexing Tab */}
              {activeTab === 'indexing' && (
                <div className="p-6">
                  <h2 className="text-xl font-bold text-foreground mb-2">Indexing Configuration</h2>
                  <p className="text-sm text-muted-foreground mb-6">
                    Performance and chunking settings
                  </p>

                  <div className="space-y-4">
                    <SettingField
                      label="Chunk Size (tokens)"
                      value={settings.indexing.chunkSize}
                      description="Size of each code chunk for indexing"
                    />
                    <SettingField
                      label="Max File Size (MB)"
                      value={settings.indexing.maxFileSizeMB}
                      description="Files larger than this will be skipped"
                    />
                    <SettingField
                      label="Max Concurrent Jobs"
                      value={settings.indexing.maxConcurrentJobs}
                      description="Number of indexing jobs that can run simultaneously"
                    />
                    <SettingField
                      label="Embedding Batch Size"
                      value={settings.indexing.embeddingBatchSize}
                      description="Number of chunks to embed in a single batch"
                    />
                    <SettingField
                      label="Enable Parallel Processing"
                      value={settings.indexing.enableParallelProcessing ? 'Enabled' : 'Disabled'}
                    />
                    <SettingField
                      label="Max Degree of Parallelism"
                      value={settings.indexing.maxDegreeOfParallelism}
                      description="Maximum parallel tasks for file processing"
                    />
                    <SettingField
                      label="Default Token Budget"
                      value={settings.indexing.defaultTokenBudget}
                      description="Default token limit for search results"
                    />
                  </div>
                </div>
              )}

              {/* Advanced Tab */}
              {activeTab === 'advanced' && (
                <div className="p-6">
                  <h2 className="text-xl font-bold text-foreground mb-2">Advanced Settings</h2>
                  <p className="text-sm text-muted-foreground mb-6">
                    File monitoring, project resolution, and job maintenance
                  </p>

                  <div className="space-y-6">
                    {/* File Monitoring */}
                    <div>
                      <h3 className="text-lg font-semibold text-foreground mb-4">File Monitoring</h3>
                      <div className="space-y-4">
                        <SettingField
                          label="Enabled"
                          value={settings.fileMonitoring.enabled ? 'Yes' : 'No'}
                        />
                        <SettingField
                          label="Debounce (ms)"
                          value={settings.fileMonitoring.debounceMilliseconds}
                          description="Wait time before triggering reindex after file change"
                        />
                        <SettingField
                          label="Max Concurrent Reindex Operations"
                          value={settings.fileMonitoring.maxConcurrentReindexOperations}
                        />
                      </div>
                    </div>

                    {/* Project Resolution */}
                    <div>
                      <h3 className="text-lg font-semibold text-foreground mb-4">Project Resolution</h3>
                      <div className="space-y-4">
                        <SettingField
                          label="Auto Create Projects"
                          value={settings.projectResolution.autoCreate ? 'Yes' : 'No'}
                        />
                        <SettingField
                          label="Auto Index Projects"
                          value={settings.projectResolution.autoIndex ? 'Yes' : 'No'}
                        />
                        <SettingField
                          label="Max Project Size (GB)"
                          value={settings.projectResolution.maxSizeGB}
                        />
                      </div>
                    </div>

                    {/* Job Maintenance */}
                    <div>
                      <h3 className="text-lg font-semibold text-foreground mb-4">Job Maintenance</h3>
                      <div className="space-y-4">
                        <SettingField
                          label="Max Jobs Per Project"
                          value={settings.jobMaintenance.maxJobsPerProject}
                        />
                        <SettingField
                          label="Job Retention (days)"
                          value={settings.jobMaintenance.jobRetentionDays}
                        />
                        <SettingField
                          label="Automatic Cleanup"
                          value={settings.jobMaintenance.enableAutomaticCleanup ? 'Enabled' : 'Disabled'}
                        />
                      </div>
                    </div>

                    {/* System */}
                    <div>
                      <h3 className="text-lg font-semibold text-foreground mb-4">System</h3>
                      <div className="space-y-4">
                        <SettingField label="Base URL" value={settings.system.baseUrl} monospace />
                        <SettingField
                          label="Auto Resume Indexing"
                          value={settings.system.autoResumeIndexing ? 'Yes' : 'No'}
                        />
                      </div>
                    </div>

                    {/* Tag Seeds */}
                    <div>
                      <h3 className="text-lg font-semibold text-foreground mb-4">Tag Seeds</h3>
                      <p className="text-sm text-muted-foreground mb-4">
                        Run the deterministic tag seeding pipeline to refresh vocabulary, rules, pipelines,
                        and personas. Use this if tags drift or new environments need baseline metadata.
                      </p>
                      <button
                        onClick={() => seedTags.mutate()}
                        disabled={seedTags.isPending}
                        className="inline-flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50"
                      >
                        {seedTags.isPending ? (
                          <Loader2 className="w-4 h-4 animate-spin" />
                        ) : (
                          <Sparkles className="w-4 h-4" />
                        )}
                        Force Import Seed Tags
                      </button>

                      {seedTags.data && (
                        <div className="mt-4 border border-border rounded-lg p-4 bg-muted/40">
                          <p className="text-sm text-muted-foreground">
                            Last run: {formatSeedTimestamp(seedTags.data.timestamp)} (forced: {seedTags.data.forced ? 'yes' : 'no'})
                          </p>
                          <div className="mt-4 overflow-x-auto">
                            <table className="min-w-full text-sm">
                              <thead>
                                <tr className="text-left text-muted-foreground">
                                  <th className="pb-2 pr-4">Segment</th>
                                  <th className="pb-2 pr-4">Created</th>
                                  <th className="pb-2">Updated</th>
                                </tr>
                              </thead>
                              <tbody>
                                {seedTags.data.reports.map((report) => (
                                  <tr key={report.segment} className="border-t border-border/60">
                                    <td className="py-2 pr-4 font-medium text-foreground capitalize">{report.segment}</td>
                                    <td className="py-2 pr-4 text-foreground">{report.created}</td>
                                    <td className="py-2 text-foreground">{report.updated}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        </div>
                      )}

                      {seedTags.isError && (
                        <div className="mt-4 border border-danger-200 bg-danger-50 text-sm text-danger-800 rounded-lg p-3">
                          {(seedTags.error instanceof Error ? seedTags.error.message : 'Failed to seed tags')}
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Footer Info */}
        <div className="mt-6 bg-muted/30 border border-border rounded-lg p-4">
          <div className="flex items-start gap-3">
            <AlertCircle className="w-5 h-5 text-muted-foreground mt-0.5" />
            <div className="text-sm text-muted-foreground">
              <p className="font-medium text-foreground mb-1">Configuration File</p>
              <p>
                To modify these settings, edit <code className="px-2 py-1 bg-muted rounded font-mono text-xs">appsettings.json</code> and restart the application.
                Runtime configuration updates are planned for a future release.
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// Helper component for displaying setting fields
function SettingField({
  label,
  value,
  description,
  monospace = false,
}: {
  label: string;
  value: string | number;
  description?: string;
  monospace?: boolean;
}) {
  return (
    <div className="border-b border-border pb-4 last:border-0 last:pb-0">
      <div className="flex items-start justify-between">
        <div className="flex-1">
          <label className="text-sm font-medium text-foreground block mb-1">{label}</label>
          {description && <p className="text-xs text-muted-foreground mb-2">{description}</p>}
        </div>
        <div className={`text-sm text-foreground ml-4 ${monospace ? 'font-mono bg-muted px-2 py-1 rounded' : 'font-medium'}`}>
          {value}
        </div>
      </div>
    </div>
  );
}
