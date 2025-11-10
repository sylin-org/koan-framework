import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useKeyboardShortcuts } from './hooks/useKeyboardShortcuts';
import Layout from './components/Layout';
import ToastContainer from './components/ToastContainer';

// Pages
import Dashboard from './pages/Dashboard';
import ProjectsList from './pages/ProjectsList';
import ProjectDetail from './pages/ProjectDetail';
import SearchPage from './pages/SearchPage';
import JobsList from './pages/JobsList';
import JobDetail from './pages/JobDetail';
import SettingsPage from './pages/SettingsPage';
import DocsPage from './pages/DocsPage';
import SearchProfilesPage from './pages/SearchProfilesPage';

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

        {/* Projects */}
        <Route path="/projects" element={<ProjectsList />} />
        <Route path="/projects/:id" element={<ProjectDetail />} />

        {/* Jobs */}
        <Route path="/jobs" element={<JobsList />} />
        <Route path="/jobs/:id" element={<JobDetail />} />

        {/* Settings */}
        <Route path="/settings" element={<SettingsPage />} />

        {/* Search Profiles */}
        <Route path="/search-profiles" element={<SearchProfilesPage />} />

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
