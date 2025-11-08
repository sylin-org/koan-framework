import { useState } from 'react';
import { useCreateProject } from '@/hooks/useProjects';
import { useToast } from '@/hooks/useToast';
import { X, FolderOpen, AlertCircle } from 'lucide-react';

interface CreateProjectModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function CreateProjectModal({ isOpen, onClose }: CreateProjectModalProps) {
  const [formData, setFormData] = useState({
    name: '',
    rootPath: '',
    docsPath: '',
  });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const createProject = useCreateProject();
  const toast = useToast();

  const validatePath = (path: string): string | null => {
    if (!path) return 'Path is required';

    // Basic path validation - Windows and Unix paths
    const windowsPathRegex = /^[a-zA-Z]:\\(?:[^<>:"|?*\n\r]+\\)*[^<>:"|?*\n\r]*$/;
    const unixPathRegex = /^\/(?:[^/\0]+\/)*[^/\0]*$/;
    const relativePathRegex = /^\.{1,2}(?:\/[^/\0]+)*$/;

    if (!windowsPathRegex.test(path) && !unixPathRegex.test(path) && !relativePathRegex.test(path)) {
      return 'Invalid path format';
    }

    return null;
  };

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {};

    // Validate name
    if (!formData.name.trim()) {
      newErrors.name = 'Project name is required';
    } else if (formData.name.length < 2) {
      newErrors.name = 'Project name must be at least 2 characters';
    } else if (formData.name.length > 100) {
      newErrors.name = 'Project name must be less than 100 characters';
    }

    // Validate rootPath
    const rootPathError = validatePath(formData.rootPath);
    if (rootPathError) {
      newErrors.rootPath = rootPathError;
    }

    // Validate docsPath (optional, but must be valid if provided)
    if (formData.docsPath.trim()) {
      const docsPathError = validatePath(formData.docsPath);
      if (docsPathError) {
        newErrors.docsPath = docsPathError;
      }
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!validateForm()) {
      return;
    }

    try {
      await createProject.mutateAsync({
        name: formData.name.trim(),
        rootPath: formData.rootPath.trim(),
        docsPath: formData.docsPath.trim() || null,
      });

      toast.success('Project Created', `Successfully created project "${formData.name.trim()}"`);

      // Reset form
      setFormData({ name: '', rootPath: '', docsPath: '' });
      setErrors({});

      // Close modal
      onClose();
    } catch (error) {
      toast.error('Create Failed', error instanceof Error ? error.message : 'Failed to create project');
    }
  };

  const handleClose = () => {
    // Reset form and errors when closing
    setFormData({ name: '', rootPath: '', docsPath: '' });
    setErrors({});
    createProject.reset();
    onClose();
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
      <div className="bg-card border border-border rounded-lg w-full max-w-lg shadow-xl">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-border">
          <div className="flex items-center gap-3">
            <FolderOpen className="w-6 h-6 text-primary-600" />
            <h2 className="text-xl font-semibold text-foreground">Create New Project</h2>
          </div>
          <button
            onClick={handleClose}
            className="text-muted-foreground hover:text-foreground transition-colors"
            disabled={createProject.isPending}
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {/* Error Banner (from mutation) */}
          {createProject.isError && (
            <div className="bg-danger-50 border border-danger-200 rounded-lg p-4 flex items-start gap-3">
              <AlertCircle className="w-5 h-5 text-danger-600 mt-0.5 flex-shrink-0" />
              <div className="flex-1">
                <p className="text-sm text-danger-900 font-medium">Failed to create project</p>
                <p className="text-sm text-danger-700 mt-1">
                  {createProject.error instanceof Error
                    ? createProject.error.message
                    : 'An unknown error occurred'}
                </p>
              </div>
            </div>
          )}

          {/* Project Name */}
          <div>
            <label htmlFor="name" className="block text-sm font-medium text-foreground mb-1">
              Project Name <span className="text-danger-600">*</span>
            </label>
            <input
              id="name"
              type="text"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              className={`w-full px-3 py-2 border rounded-lg focus:outline-none focus:ring-2 ${
                errors.name
                  ? 'border-danger-300 focus:ring-danger-500'
                  : 'border-border focus:ring-primary-500'
              }`}
              placeholder="My Code Project"
              disabled={createProject.isPending}
              autoFocus
            />
            {errors.name && (
              <p className="text-sm text-danger-600 mt-1">{errors.name}</p>
            )}
          </div>

          {/* Root Path */}
          <div>
            <label htmlFor="rootPath" className="block text-sm font-medium text-foreground mb-1">
              Root Path <span className="text-danger-600">*</span>
            </label>
            <input
              id="rootPath"
              type="text"
              value={formData.rootPath}
              onChange={(e) => setFormData({ ...formData, rootPath: e.target.value })}
              className={`w-full px-3 py-2 border rounded-lg focus:outline-none focus:ring-2 font-mono text-sm ${
                errors.rootPath
                  ? 'border-danger-300 focus:ring-danger-500'
                  : 'border-border focus:ring-primary-500'
              }`}
              placeholder="C:\Projects\MyApp or /home/user/projects/myapp"
              disabled={createProject.isPending}
            />
            {errors.rootPath && (
              <p className="text-sm text-danger-600 mt-1">{errors.rootPath}</p>
            )}
            <p className="text-xs text-muted-foreground mt-1">
              The root directory of your codebase to index
            </p>
          </div>

          {/* Docs Path (Optional) */}
          <div>
            <label htmlFor="docsPath" className="block text-sm font-medium text-foreground mb-1">
              Documentation Path <span className="text-muted-foreground">(Optional)</span>
            </label>
            <input
              id="docsPath"
              type="text"
              value={formData.docsPath}
              onChange={(e) => setFormData({ ...formData, docsPath: e.target.value })}
              className={`w-full px-3 py-2 border rounded-lg focus:outline-none focus:ring-2 font-mono text-sm ${
                errors.docsPath
                  ? 'border-danger-300 focus:ring-danger-500'
                  : 'border-border focus:ring-primary-500'
              }`}
              placeholder="./docs or C:\Projects\MyApp\docs"
              disabled={createProject.isPending}
            />
            {errors.docsPath && (
              <p className="text-sm text-danger-600 mt-1">{errors.docsPath}</p>
            )}
            <p className="text-xs text-muted-foreground mt-1">
              Optional path to documentation files (relative or absolute)
            </p>
          </div>

          {/* Form Actions */}
          <div className="flex items-center justify-end gap-3 pt-4 border-t border-border">
            <button
              type="button"
              onClick={handleClose}
              className="px-4 py-2 border border-border rounded-lg hover:bg-muted transition-colors"
              disabled={createProject.isPending}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
              disabled={createProject.isPending}
            >
              {createProject.isPending ? (
                <span className="flex items-center gap-2">
                  <Loader2 className="w-4 h-4 animate-spin" />
                  Creating...
                </span>
              ) : (
                'Create Project'
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function Loader2({ className }: { className?: string }) {
  return (
    <svg
      className={className}
      xmlns="http://www.w3.org/2000/svg"
      width="24"
      height="24"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M21 12a9 9 0 1 1-6.219-8.56" />
    </svg>
  );
}
