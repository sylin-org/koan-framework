import { useState } from 'react';
import { X, AlertCircle } from 'lucide-react';
import type { SearchAudience, SearchCategory, CreateSearchAudienceRequest, UpdateSearchAudienceRequest } from '@/api/types';

interface AudienceFormProps {
  audience?: SearchAudience | null;
  categories: SearchCategory[];
  onSubmit: (data: CreateSearchAudienceRequest | UpdateSearchAudienceRequest) => Promise<void>;
  onCancel: () => void;
  isLoading?: boolean;
}

const COMMON_ICONS = ['🎓', '👨‍💻', '🏗️', '📊', '👔', '🤝', '💼', '🎯', '🔬', '📝'];

export default function AudienceForm({ audience, categories, onSubmit, onCancel, isLoading = false }: AudienceFormProps) {
  const isEdit = !!audience;

  // Form state
  const [name, setName] = useState(audience?.name || '');
  const [displayName, setDisplayName] = useState(audience?.displayName || '');
  const [description, setDescription] = useState(audience?.description || '');
  const [selectedCategories, setSelectedCategories] = useState<Set<string>>(
    new Set(audience?.categoryNames || [])
  );
  const [defaultAlpha, setDefaultAlpha] = useState(audience?.defaultAlpha?.toString() || '0.5');
  const [maxTokens, setMaxTokens] = useState(audience?.maxTokens?.toString() || '5000');
  const [includeReasoning, setIncludeReasoning] = useState(audience?.includeReasoning ?? true);
  const [includeInsights, setIncludeInsights] = useState(audience?.includeInsights ?? true);
  const [isActive, setIsActive] = useState(audience?.isActive ?? true);
  const [icon, setIcon] = useState(audience?.icon || '');

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

    const alphaNum = parseFloat(defaultAlpha);
    if (isNaN(alphaNum) || alphaNum < 0 || alphaNum > 1) {
      newErrors.defaultAlpha = 'Default alpha must be between 0.0 and 1.0';
    }

    const tokensNum = parseInt(maxTokens, 10);
    if (isNaN(tokensNum) || tokensNum < 1000 || tokensNum > 20000) {
      newErrors.maxTokens = 'Max tokens must be between 1000 and 20000';
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!validate()) {
      return;
    }

    const data = {
      name: name.trim(),
      displayName: displayName.trim(),
      description: description.trim(),
      categoryNames: Array.from(selectedCategories),
      defaultAlpha: parseFloat(defaultAlpha),
      maxTokens: parseInt(maxTokens, 10),
      includeReasoning,
      includeInsights,
      isActive,
      icon: icon.trim() || undefined,
    };

    await onSubmit(data);
  };

  const toggleCategory = (categoryName: string) => {
    const newSelected = new Set(selectedCategories);
    if (newSelected.has(categoryName)) {
      newSelected.delete(categoryName);
    } else {
      newSelected.add(categoryName);
    }
    setSelectedCategories(newSelected);
  };

  const sortedCategories = categories
    .filter((c) => c.isActive)
    .sort((a, b) => b.priority - a.priority);

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4 overflow-y-auto">
      <div className="bg-card border border-border rounded-lg w-full max-w-3xl shadow-xl my-8">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-border sticky top-0 bg-card z-10 rounded-t-lg">
          <h2 className="text-xl font-semibold text-foreground">
            {isEdit ? 'Edit Audience' : 'Create New Audience'}
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
                placeholder="learner"
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
                placeholder="Developer Learning Koan"
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
              placeholder="New developers learning the framework"
            />
            {errors.description && (
              <p className="mt-1 text-sm text-danger-600 flex items-center gap-1">
                <AlertCircle className="w-3 h-3" />
                {errors.description}
              </p>
            )}
          </div>

          {/* Categories */}
          <div>
            <label className="block text-sm font-medium text-foreground mb-2">
              Categories
            </label>
            <div className="bg-background border border-border rounded-lg p-4 max-h-64 overflow-y-auto">
              {sortedCategories.length > 0 ? (
                <div className="space-y-2">
                  {sortedCategories.map((category) => (
                    <label
                      key={category.id}
                      className="flex items-center gap-3 p-2 rounded-lg hover:bg-muted cursor-pointer transition-colors"
                    >
                      <input
                        type="checkbox"
                        checked={selectedCategories.has(category.name)}
                        onChange={() => toggleCategory(category.name)}
                        className="w-4 h-4 text-primary-600 bg-background border-border rounded focus:ring-primary-500"
                      />
                      <div className="flex items-center gap-2 flex-1">
                        {category.icon && <span>{category.icon}</span>}
                        <div className="flex-1">
                          <div className="text-sm font-medium text-foreground">
                            {category.displayName}
                          </div>
                          <div className="text-xs text-muted-foreground">
                            {category.description}
                          </div>
                        </div>
                      </div>
                    </label>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-muted-foreground text-center py-4">
                  No active categories available. Create categories first.
                </p>
              )}
            </div>
            <p className="mt-2 text-xs text-muted-foreground">
              {selectedCategories.size} categor{selectedCategories.size === 1 ? 'y' : 'ies'} selected
            </p>
          </div>

          {/* Default Alpha & Max Tokens */}
          <div className="grid grid-cols-2 gap-4">
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

            <div>
              <label className="block text-sm font-medium text-foreground mb-2">
                Max Tokens <span className="text-danger-600">*</span>
              </label>
              <input
                type="number"
                value={maxTokens}
                onChange={(e) => setMaxTokens(e.target.value)}
                min="1000"
                max="20000"
                step="100"
                className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
              {errors.maxTokens && (
                <p className="mt-1 text-sm text-danger-600 flex items-center gap-1">
                  <AlertCircle className="w-3 h-3" />
                  {errors.maxTokens}
                </p>
              )}
              <p className="mt-1 text-xs text-muted-foreground">Result verbosity (1000-20000)</p>
            </div>
          </div>

          {/* Icon */}
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
                placeholder="🎓"
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

          {/* Metadata Options */}
          <div className="space-y-3">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={includeReasoning}
                onChange={(e) => setIncludeReasoning(e.target.checked)}
                className="w-4 h-4 text-primary-600 bg-background border-border rounded focus:ring-primary-500"
              />
              <span className="text-sm font-medium text-foreground">Include Reasoning</span>
            </label>
            <p className="ml-6 text-xs text-muted-foreground">
              Include retrieval reasoning metadata in search results
            </p>

            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={includeInsights}
                onChange={(e) => setIncludeInsights(e.target.checked)}
                className="w-4 h-4 text-primary-600 bg-background border-border rounded focus:ring-primary-500"
              />
              <span className="text-sm font-medium text-foreground">Include Insights</span>
            </label>
            <p className="ml-6 text-xs text-muted-foreground">
              Include insights metadata in search results
            </p>

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
              Only active audiences are available for search
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
              {isLoading ? 'Saving...' : isEdit ? 'Update Audience' : 'Create Audience'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
