import { useState } from 'react';
import { X, Plus, Trash2, AlertCircle } from 'lucide-react';
import type { SearchCategory, CreateSearchCategoryRequest, UpdateSearchCategoryRequest } from '@/api/types';

interface CategoryFormProps {
  category?: SearchCategory | null;
  onSubmit: (data: CreateSearchCategoryRequest | UpdateSearchCategoryRequest) => Promise<void>;
  onCancel: () => void;
  isLoading?: boolean;
}

const COMMON_ICONS = ['📚', '📝', '💡', '🔬', '🎯', '📖', '⚙️', '🧪', '📋', '🏷️'];
const COMMON_COLORS = [
  { name: 'Blue', value: '#3B82F6' },
  { name: 'Green', value: '#10B981' },
  { name: 'Purple', value: '#8B5CF6' },
  { name: 'Orange', value: '#F59E0B' },
  { name: 'Red', value: '#EF4444' },
  { name: 'Pink', value: '#EC4899' },
  { name: 'Indigo', value: '#6366F1' },
  { name: 'Teal', value: '#14B8A6' },
];

export default function CategoryForm({ category, onSubmit, onCancel, isLoading = false }: CategoryFormProps) {
  const isEdit = !!category;

  // Form state
  const [name, setName] = useState(category?.name || '');
  const [displayName, setDisplayName] = useState(category?.displayName || '');
  const [description, setDescription] = useState(category?.description || '');
  const [pathPatterns, setPathPatterns] = useState<string[]>(category?.pathPatterns || ['']);
  const [priority, setPriority] = useState(category?.priority?.toString() || '5');
  const [defaultAlpha, setDefaultAlpha] = useState(category?.defaultAlpha?.toString() || '0.5');
  const [isActive, setIsActive] = useState(category?.isActive ?? true);
  const [icon, setIcon] = useState(category?.icon || '');
  const [color, setColor] = useState(category?.color || '');

  // Validation errors
  const [errors, setErrors] = useState<Record<string, string>>({});

  const validate = (): boolean => {
    const newErrors: Record<string, string> = {};

    if (!name.trim()) {
      newErrors.name = 'Name is required';
    } else if (!/^[a-z0-9_-]+$/i.test(name)) {
      newErrors.name = 'Name must contain only letters, numbers, underscores, and hyphens';
    }

    if (!displayName.trim()) {
      newErrors.displayName = 'Display name is required';
    }

    if (!description.trim()) {
      newErrors.description = 'Description is required';
    }

    const validPatterns = pathPatterns.filter((p) => p.trim() !== '');
    if (validPatterns.length === 0) {
      newErrors.pathPatterns = 'At least one path pattern is required';
    }

    const priorityNum = parseInt(priority, 10);
    if (isNaN(priorityNum) || priorityNum < 0 || priorityNum > 100) {
      newErrors.priority = 'Priority must be between 0 and 100';
    }

    const alphaNum = parseFloat(defaultAlpha);
    if (isNaN(alphaNum) || alphaNum < 0 || alphaNum > 1) {
      newErrors.defaultAlpha = 'Default alpha must be between 0.0 and 1.0';
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!validate()) {
      return;
    }

    const validPatterns = pathPatterns.filter((p) => p.trim() !== '');

    const data = {
      name: name.trim(),
      displayName: displayName.trim(),
      description: description.trim(),
      pathPatterns: validPatterns,
      priority: parseInt(priority, 10),
      defaultAlpha: parseFloat(defaultAlpha),
      isActive,
      icon: icon.trim() || undefined,
      color: color.trim() || undefined,
    };

    await onSubmit(data);
  };

  const addPathPattern = () => {
    setPathPatterns([...pathPatterns, '']);
  };

  const updatePathPattern = (index: number, value: string) => {
    const newPatterns = [...pathPatterns];
    newPatterns[index] = value;
    setPathPatterns(newPatterns);
  };

  const removePathPattern = (index: number) => {
    if (pathPatterns.length > 1) {
      const newPatterns = pathPatterns.filter((_, i) => i !== index);
      setPathPatterns(newPatterns);
    }
  };

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4 overflow-y-auto">
      <div className="bg-card border border-border rounded-lg w-full max-w-3xl shadow-xl my-8">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-border sticky top-0 bg-card z-10 rounded-t-lg">
          <h2 className="text-xl font-semibold text-foreground">
            {isEdit ? 'Edit Category' : 'Create New Category'}
          </h2>
          <button
            onClick={onCancel}
            className="text-muted-foreground hover:text-foreground transition-colors"
            disabled={isLoading}
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6 space-y-6">
          {/* Name & Display Name */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-foreground mb-2">
                Name <span className="text-danger-600">*</span>
              </label>
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={isEdit} // Can't change name after creation
                className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 disabled:opacity-50 disabled:cursor-not-allowed"
                placeholder="guide"
              />
              {errors.name && (
                <p className="mt-1 text-sm text-danger-600 flex items-center gap-1">
                  <AlertCircle className="w-3 h-3" />
                  {errors.name}
                </p>
              )}
              <p className="mt-1 text-xs text-muted-foreground">
                Unique identifier (lowercase, no spaces)
              </p>
            </div>

            <div>
              <label className="block text-sm font-medium text-foreground mb-2">
                Display Name <span className="text-danger-600">*</span>
              </label>
              <input
                type="text"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                placeholder="Developer Guides"
              />
              {errors.displayName && (
                <p className="mt-1 text-sm text-danger-600 flex items-center gap-1">
                  <AlertCircle className="w-3 h-3" />
                  {errors.displayName}
                </p>
              )}
              <p className="mt-1 text-xs text-muted-foreground">Human-readable name</p>
            </div>
          </div>

          {/* Description */}
          <div>
            <label className="block text-sm font-medium text-foreground mb-2">
              Description <span className="text-danger-600">*</span>
            </label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
              placeholder="Step-by-step developer guides and tutorials"
            />
            {errors.description && (
              <p className="mt-1 text-sm text-danger-600 flex items-center gap-1">
                <AlertCircle className="w-3 h-3" />
                {errors.description}
              </p>
            )}
          </div>

          {/* Path Patterns */}
          <div>
            <label className="block text-sm font-medium text-foreground mb-2">
              Path Patterns <span className="text-danger-600">*</span>
            </label>
            <div className="space-y-2">
              {pathPatterns.map((pattern, index) => (
                <div key={index} className="flex gap-2">
                  <input
                    type="text"
                    value={pattern}
                    onChange={(e) => updatePathPattern(index, e.target.value)}
                    className="flex-1 px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 font-mono text-sm"
                    placeholder="docs/guides/**"
                  />
                  {pathPatterns.length > 1 && (
                    <button
                      type="button"
                      onClick={() => removePathPattern(index)}
                      className="p-2 text-danger-600 hover:bg-danger-50 rounded-lg transition-colors"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  )}
                </div>
              ))}
            </div>
            {errors.pathPatterns && (
              <p className="mt-1 text-sm text-danger-600 flex items-center gap-1">
                <AlertCircle className="w-3 h-3" />
                {errors.pathPatterns}
              </p>
            )}
            <button
              type="button"
              onClick={addPathPattern}
              className="mt-2 flex items-center gap-2 text-sm text-primary-600 hover:text-primary-700"
            >
              <Plus className="w-4 h-4" />
              Add Pattern
            </button>
            <p className="mt-2 text-xs text-muted-foreground">
              Glob patterns: ** = any subdirectories, * = any characters
            </p>
          </div>

          {/* Priority & Default Alpha */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-foreground mb-2">
                Priority <span className="text-danger-600">*</span>
              </label>
              <input
                type="number"
                value={priority}
                onChange={(e) => setPriority(e.target.value)}
                min="0"
                max="100"
                step="1"
                className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
              {errors.priority && (
                <p className="mt-1 text-sm text-danger-600 flex items-center gap-1">
                  <AlertCircle className="w-3 h-3" />
                  {errors.priority}
                </p>
              )}
              <p className="mt-1 text-xs text-muted-foreground">Higher = matched first (0-100)</p>
            </div>

            <div>
              <label className="block text-sm font-medium text-foreground mb-2">
                Default Alpha <span className="text-danger-600">*</span>
              </label>
              <input
                type="number"
                value={defaultAlpha}
                onChange={(e) => setDefaultAlpha(e.target.value)}
                min="0"
                max="1"
                step="0.1"
                className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
              {errors.defaultAlpha && (
                <p className="mt-1 text-sm text-danger-600 flex items-center gap-1">
                  <AlertCircle className="w-3 h-3" />
                  {errors.defaultAlpha}
                </p>
              )}
              <p className="mt-1 text-xs text-muted-foreground">
                Semantic weight: 0.0 = keyword, 1.0 = semantic
              </p>
            </div>
          </div>

          {/* Icon & Color */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-foreground mb-2">
                Icon (optional)
              </label>
              <div className="space-y-2">
                <input
                  type="text"
                  value={icon}
                  onChange={(e) => setIcon(e.target.value)}
                  className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                  placeholder="📚"
                  maxLength={2}
                />
                <div className="flex flex-wrap gap-2">
                  {COMMON_ICONS.map((emoji) => (
                    <button
                      key={emoji}
                      type="button"
                      onClick={() => setIcon(emoji)}
                      className={`w-8 h-8 rounded border transition-colors ${
                        icon === emoji
                          ? 'border-primary-600 bg-primary-50'
                          : 'border-border hover:border-primary-300'
                      }`}
                    >
                      {emoji}
                    </button>
                  ))}
                </div>
              </div>
            </div>

            <div>
              <label className="block text-sm font-medium text-foreground mb-2">
                Color (optional)
              </label>
              <div className="space-y-2">
                <input
                  type="text"
                  value={color}
                  onChange={(e) => setColor(e.target.value)}
                  className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 font-mono"
                  placeholder="#3B82F6"
                />
                <div className="flex flex-wrap gap-2">
                  {COMMON_COLORS.map((c) => (
                    <button
                      key={c.value}
                      type="button"
                      onClick={() => setColor(c.value)}
                      className={`w-8 h-8 rounded border transition-colors ${
                        color === c.value
                          ? 'border-foreground ring-2 ring-primary-500'
                          : 'border-border hover:border-foreground'
                      }`}
                      style={{ backgroundColor: c.value }}
                      title={c.name}
                    />
                  ))}
                </div>
              </div>
            </div>
          </div>

          {/* Active Status */}
          <div>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                className="w-4 h-4 text-primary-600 bg-background border-border rounded focus:ring-primary-500"
              />
              <span className="text-sm font-medium text-foreground">Active</span>
            </label>
            <p className="ml-6 text-xs text-muted-foreground">
              Only active categories are used for classification
            </p>
          </div>

          {/* Actions */}
          <div className="flex items-center justify-end gap-3 pt-4 border-t border-border">
            <button
              type="button"
              onClick={onCancel}
              className="px-4 py-2 border border-border rounded-lg hover:bg-muted transition-colors"
              disabled={isLoading}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
              disabled={isLoading}
            >
              {isLoading ? 'Saving...' : isEdit ? 'Update Category' : 'Create Category'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
