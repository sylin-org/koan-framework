import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useKeyboardShortcuts } from './hooks/useKeyboardShortcuts';
import Layout from './components/Layout';
import ToastContainer from './components/ToastContainer';

// Pages
import Dashboard from './pages/Dashboard';
import MonitoringDashboard from './pages/MonitoringDashboard';
import ProjectsList from './pages/ProjectsList';
import ProjectDetail from './pages/ProjectDetail';
import SearchPage from './pages/SearchPage';
import JobsList from './pages/JobsList';
import JobDetail from './pages/JobDetail';
import SettingsPage from './pages/SettingsPage';
import DocsPage from './pages/DocsPage';
import TagsPage from '@/modules/tags/TagsPage';
import TagGovernancePage from '@/modules/tag-governance/TagGovernancePage';
import TagRulesPage from '@/modules/tag-rules/TagRulesPage';
import TagPipelinesPage from '@/modules/tag-pipelines/TagPipelinesPage';
import SearchPersonasPage from '@/modules/search-personas/SearchPersonasPage';

// Create TanStack Query client
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30000, // 30 seconds
      refetchOnWindowFocus: false,
    },
  },
});

function AppContent() {
  // Enable global keyboard shortcuts
  useKeyboardShortcuts();

  return (
    <Layout>
      <Routes>
        {/* Search - Primary Interface */}
        <Route path="/" element={<SearchPage />} />

        {/* Dashboard */}
        <Route path="/dashboard" element={<Dashboard />} />
        <Route path="/monitoring" element={<MonitoringDashboard />} />

        {/* Projects */}
        <Route path="/projects" element={<ProjectsList />} />
        <Route path="/projects/:id" element={<ProjectDetail />} />

        {/* Jobs */}
        <Route path="/jobs" element={<JobsList />} />
        <Route path="/jobs/:id" element={<JobDetail />} />

        {/* Settings */}
        <Route path="/settings" element={<SettingsPage />} />

    {/* Tags */}
    <Route path="/tags" element={<TagsPage />} />
    <Route path="/tags/governance" element={<TagGovernancePage />} />
    <Route path="/tags/rules" element={<TagRulesPage />} />
    <Route path="/tags/pipelines" element={<TagPipelinesPage />} />
    <Route path="/tags/personas" element={<SearchPersonasPage />} />

        {/* Docs & Support */}
        <Route path="/docs" element={<DocsPage />} />
        <Route path="/support" element={<div className="p-8"><h1 className="text-2xl font-bold">Support</h1></div>} />
      </Routes>
    </Layout>
  );
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AppContent />
        <ToastContainer />
      </BrowserRouter>
    </QueryClientProvider>
  );
}

export default App;
