import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { searchProfilesApi, type SearchCategory, type SearchAudience } from '@/api';
import { useToast } from '@/hooks/useToast';
import ConfirmDialog from '@/components/ConfirmDialog';
import CategoryForm from '@/components/CategoryForm';
import AudienceForm from '@/components/AudienceForm';
import {
  BookOpen,
  Users,
  Plus,
  Trash2,
  Edit2,
  Power,
  PowerOff,
  ChevronRight,
  Loader2,
  AlertCircle,
  Tag,
  Target,
} from 'lucide-react';

type Tab = 'categories' | 'audiences';
type FormMode = 'create' | 'edit' | null;

export default function SearchProfilesPage() {
  const [activeTab, setActiveTab] = useState<Tab>('categories');
  const [selectedCategory, setSelectedCategory] = useState<SearchCategory | null>(null);
  const [selectedAudience, setSelectedAudience] = useState<SearchAudience | null>(null);
  const [formMode, setFormMode] = useState<FormMode>(null);
  const [itemToDelete, setItemToDelete] = useState<{ type: 'category' | 'audience'; id: string; name: string } | null>(null);

  const queryClient = useQueryClient();
  const toast = useToast();

  // Query categories
  const { data: categories, isLoading: categoriesLoading, error: categoriesError } = useQuery({
    queryKey: ['searchCategories'],
    queryFn: () => searchProfilesApi.listCategories(),
  });

  // Query audiences
  const { data: audiences, isLoading: audiencesLoading, error: audiencesError } = useQuery({
    queryKey: ['searchAudiences'],
    queryFn: () => searchProfilesApi.listAudiences(),
  });

  // Create mutations
  const createCategory = useMutation({
    mutationFn: searchProfilesApi.createCategory,
    onSuccess: (newCategory) => {
      queryClient.invalidateQueries({ queryKey: ['searchCategories'] });
      toast.success('Category Created', `Successfully created "${newCategory.displayName}"`);
      setFormMode(null);
      setSelectedCategory(newCategory);
    },
    onError: (error: any) => {
      toast.error('Creation Failed', error.error || 'Failed to create category');
    },
  });

  const createAudience = useMutation({
    mutationFn: searchProfilesApi.createAudience,
    onSuccess: (newAudience) => {
      queryClient.invalidateQueries({ queryKey: ['searchAudiences'] });
      toast.success('Audience Created', `Successfully created "${newAudience.displayName}"`);
      setFormMode(null);
      setSelectedAudience(newAudience);
    },
    onError: (error: any) => {
      toast.error('Creation Failed', error.error || 'Failed to create audience');
    },
  });

  // Update mutations
  const updateCategory = useMutation({
    mutationFn: ({ id, data }: { id: string; data: any }) =>
      searchProfilesApi.updateCategory(id, data),
    onSuccess: (updatedCategory) => {
      queryClient.invalidateQueries({ queryKey: ['searchCategories'] });
      toast.success('Category Updated', `Successfully updated "${updatedCategory.displayName}"`);
      setFormMode(null);
      setSelectedCategory(updatedCategory);
    },
    onError: (error: any) => {
      toast.error('Update Failed', error.error || 'Failed to update category');
    },
  });

  const updateAudience = useMutation({
    mutationFn: ({ id, data }: { id: string; data: any }) =>
      searchProfilesApi.updateAudience(id, data),
    onSuccess: (updatedAudience) => {
      queryClient.invalidateQueries({ queryKey: ['searchAudiences'] });
      toast.success('Audience Updated', `Successfully updated "${updatedAudience.displayName}"`);
      setFormMode(null);
      setSelectedAudience(updatedAudience);
    },
    onError: (error: any) => {
      toast.error('Update Failed', error.error || 'Failed to update audience');
    },
  });

  // Delete mutations
  const deleteCategory = useMutation({
    mutationFn: (id: string) => searchProfilesApi.deleteCategory(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['searchCategories'] });
      toast.success('Category Deleted', 'Search category deleted successfully');
      setSelectedCategory(null);
      setItemToDelete(null);
    },
    onError: (error: any) => {
      toast.error('Delete Failed', error.error || 'Failed to delete category');
    },
  });

  const deleteAudience = useMutation({
    mutationFn: (id: string) => searchProfilesApi.deleteAudience(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['searchAudiences'] });
      toast.success('Audience Deleted', 'Search audience deleted successfully');
      setSelectedAudience(null);
      setItemToDelete(null);
    },
    onError: (error: any) => {
      toast.error('Delete Failed', error.error || 'Failed to delete audience');
    },
  });

  // Toggle active mutations
  const toggleCategoryActive = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      searchProfilesApi.toggleCategoryActive(id, isActive),
    onSuccess: (updated) => {
      queryClient.invalidateQueries({ queryKey: ['searchCategories'] });
      setSelectedCategory(updated);
      toast.success('Status Updated', `Category is now ${updated.isActive ? 'active' : 'inactive'}`);
    },
  });

  const toggleAudienceActive = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      searchProfilesApi.toggleAudienceActive(id, isActive),
    onSuccess: (updated) => {
      queryClient.invalidateQueries({ queryKey: ['searchAudiences'] });
      setSelectedAudience(updated);
      toast.success('Status Updated', `Audience is now ${updated.isActive ? 'active' : 'inactive'}`);
    },
  });

  const handleDeleteConfirm = async () => {
    if (!itemToDelete) return;

    if (itemToDelete.type === 'category') {
      await deleteCategory.mutateAsync(itemToDelete.id);
    } else {
      await deleteAudience.mutateAsync(itemToDelete.id);
    }
  };

  const handleCategorySelect = (category: SearchCategory) => {
    setSelectedCategory(category);
    setSelectedAudience(null);
    setFormMode(null);
  };

  const handleAudienceSelect = (audience: SearchAudience) => {
    setSelectedAudience(audience);
    setSelectedCategory(null);
    setFormMode(null);
  };

  const handleNewCategory = () => {
    setSelectedCategory(null);
    setSelectedAudience(null);
    setFormMode('create');
    setActiveTab('categories');
  };

  const handleNewAudience = () => {
    setSelectedCategory(null);
    setSelectedAudience(null);
    setFormMode('create');
    setActiveTab('audiences');
  };

  const handleEditCategory = () => {
    setFormMode('edit');
  };

  const handleEditAudience = () => {
    setFormMode('edit');
  };

  const handleCategoryFormSubmit = async (data: any) => {
    if (formMode === 'create') {
      await createCategory.mutateAsync(data);
    } else if (formMode === 'edit' && selectedCategory) {
      await updateCategory.mutateAsync({ id: selectedCategory.id, data });
    }
  };

  const handleAudienceFormSubmit = async (data: any) => {
    if (formMode === 'create') {
      await createAudience.mutateAsync(data);
    } else if (formMode === 'edit' && selectedAudience) {
      await updateAudience.mutateAsync({ id: selectedAudience.id, data });
    }
  };

  const sortedCategories = categories
    ?.slice()
    .sort((a, b) => b.priority - a.priority);

  const sortedAudiences = audiences
    ?.slice()
    .sort((a, b) => a.displayName.localeCompare(b.displayName));

  return (
    <div className="h-full flex flex-col bg-background">
      {/* Header */}
      <div className="px-6 py-4 border-b border-border bg-card">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-bold text-foreground">Search Profiles</h1>
            <p className="text-sm text-muted-foreground mt-1">
              Manage search categories and audience profiles for intelligent content filtering
            </p>
          </div>
          <button
            onClick={activeTab === 'categories' ? handleNewCategory : handleNewAudience}
            className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
          >
            <Plus className="w-4 h-4" />
            New {activeTab === 'categories' ? 'Category' : 'Audience'}
          </button>
        </div>

        {/* Tabs */}
        <div className="flex gap-4 mt-4 border-b border-border">
          <button
            onClick={() => setActiveTab('categories')}
            className={`flex items-center gap-2 px-4 py-2 border-b-2 transition-colors ${
              activeTab === 'categories'
                ? 'border-primary-600 text-primary-600'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            }`}
          >
            <Tag className="w-4 h-4" />
            Categories
            {categories && (
              <span className="ml-1 px-2 py-0.5 bg-muted text-muted-foreground text-xs rounded-full">
                {categories.length}
              </span>
            )}
          </button>
          <button
            onClick={() => setActiveTab('audiences')}
            className={`flex items-center gap-2 px-4 py-2 border-b-2 transition-colors ${
              activeTab === 'audiences'
                ? 'border-primary-600 text-primary-600'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            }`}
          >
            <Target className="w-4 h-4" />
            Audiences
            {audiences && (
              <span className="ml-1 px-2 py-0.5 bg-muted text-muted-foreground text-xs rounded-full">
                {audiences.length}
              </span>
            )}
          </button>
        </div>
      </div>

      {/* Master/Detail Layout */}
      <div className="flex-1 flex overflow-hidden">
        {/* Master List */}
        <div className="w-1/3 border-r border-border overflow-y-auto bg-card">
          {activeTab === 'categories' ? (
            // Categories List
            categoriesLoading ? (
              <div className="p-8 text-center">
                <Loader2 className="w-8 h-8 animate-spin mx-auto text-muted-foreground" />
                <p className="mt-2 text-sm text-muted-foreground">Loading categories...</p>
              </div>
            ) : categoriesError ? (
              <div className="p-8 text-center">
                <AlertCircle className="w-8 h-8 mx-auto text-danger-600" />
                <p className="mt-2 text-sm text-danger-600">Failed to load categories</p>
              </div>
            ) : sortedCategories && sortedCategories.length > 0 ? (
              <div className="divide-y divide-border">
                {sortedCategories.map((category) => (
                  <button
                    key={category.id}
                    onClick={() => handleCategorySelect(category)}
                    className={`w-full px-4 py-3 text-left hover:bg-muted/50 transition-colors ${
                      selectedCategory?.id === category.id ? 'bg-muted' : ''
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2 flex-1 min-w-0">
                        {category.icon && (
                          <span className="flex-shrink-0">{category.icon}</span>
                        )}
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <span className="font-medium text-foreground truncate">
                              {category.displayName}
                            </span>
                            {!category.isActive && (
                              <PowerOff className="w-3 h-3 text-muted-foreground flex-shrink-0" />
                            )}
                          </div>
                          <p className="text-xs text-muted-foreground truncate">
                            Priority: {category.priority} • Alpha: {category.defaultAlpha}
                          </p>
                        </div>
                      </div>
                      <ChevronRight className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                    </div>
                  </button>
                ))}
              </div>
            ) : (
              <div className="p-8 text-center">
                <BookOpen className="w-12 h-12 mx-auto text-muted-foreground" />
                <p className="mt-2 text-sm text-muted-foreground">No categories found</p>
                <button
                  onClick={handleNewCategory}
                  className="mt-4 text-sm text-primary-600 hover:text-primary-700"
                >
                  Create your first category
                </button>
              </div>
            )
          ) : (
            // Audiences List
            audiencesLoading ? (
              <div className="p-8 text-center">
                <Loader2 className="w-8 h-8 animate-spin mx-auto text-muted-foreground" />
                <p className="mt-2 text-sm text-muted-foreground">Loading audiences...</p>
              </div>
            ) : audiencesError ? (
              <div className="p-8 text-center">
                <AlertCircle className="w-8 h-8 mx-auto text-danger-600" />
                <p className="mt-2 text-sm text-danger-600">Failed to load audiences</p>
              </div>
            ) : sortedAudiences && sortedAudiences.length > 0 ? (
              <div className="divide-y divide-border">
                {sortedAudiences.map((audience) => (
                  <button
                    key={audience.id}
                    onClick={() => handleAudienceSelect(audience)}
                    className={`w-full px-4 py-3 text-left hover:bg-muted/50 transition-colors ${
                      selectedAudience?.id === audience.id ? 'bg-muted' : ''
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2 flex-1 min-w-0">
                        {audience.icon && (
                          <span className="flex-shrink-0">{audience.icon}</span>
                        )}
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <span className="font-medium text-foreground truncate">
                              {audience.displayName}
                            </span>
                            {!audience.isActive && (
                              <PowerOff className="w-3 h-3 text-muted-foreground flex-shrink-0" />
                            )}
                          </div>
                          <p className="text-xs text-muted-foreground truncate">
                            {audience.categoryNames.length} categories • {audience.maxTokens} tokens
                          </p>
                        </div>
                      </div>
                      <ChevronRight className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                    </div>
                  </button>
                ))}
              </div>
            ) : (
              <div className="p-8 text-center">
                <Users className="w-12 h-12 mx-auto text-muted-foreground" />
                <p className="mt-2 text-sm text-muted-foreground">No audiences found</p>
                <button
                  onClick={handleNewAudience}
                  className="mt-4 text-sm text-primary-600 hover:text-primary-700"
                >
                  Create your first audience
                </button>
              </div>
            )
          )}
        </div>

        {/* Detail Panel */}
        <div className="flex-1 overflow-y-auto bg-background">
          {selectedCategory && !formMode && (
            <CategoryDetail
              category={selectedCategory}
              onEdit={handleEditCategory}
              onDelete={() => setItemToDelete({ type: 'category', id: selectedCategory.id, name: selectedCategory.displayName })}
              onToggleActive={(isActive) => toggleCategoryActive.mutate({ id: selectedCategory.id, isActive })}
            />
          )}

          {selectedAudience && !formMode && (
            <AudienceDetail
              audience={selectedAudience}
              categories={categories || []}
              onEdit={handleEditAudience}
              onDelete={() => setItemToDelete({ type: 'audience', id: selectedAudience.id, name: selectedAudience.displayName })}
              onToggleActive={(isActive) => toggleAudienceActive.mutate({ id: selectedAudience.id, isActive })}
            />
          )}

          {!selectedCategory && !selectedAudience && !formMode && (
            <div className="h-full flex items-center justify-center">
              <div className="text-center">
                {activeTab === 'categories' ? (
                  <BookOpen className="w-16 h-16 mx-auto text-muted-foreground" />
                ) : (
                  <Users className="w-16 h-16 mx-auto text-muted-foreground" />
                )}
                <p className="mt-4 text-lg font-medium text-foreground">
                  No {activeTab === 'categories' ? 'Category' : 'Audience'} Selected
                </p>
                <p className="mt-2 text-sm text-muted-foreground">
                  Select a {activeTab} from the list or create a new one
                </p>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Forms */}
      {formMode && activeTab === 'categories' && (
        <CategoryForm
          category={formMode === 'edit' ? selectedCategory : null}
          onSubmit={handleCategoryFormSubmit}
          onCancel={() => setFormMode(null)}
          isLoading={createCategory.isPending || updateCategory.isPending}
        />
      )}

      {formMode && activeTab === 'audiences' && (
        <AudienceForm
          audience={formMode === 'edit' ? selectedAudience : null}
          categories={categories || []}
          onSubmit={handleAudienceFormSubmit}
          onCancel={() => setFormMode(null)}
          isLoading={createAudience.isPending || updateAudience.isPending}
        />
      )}

      {/* Delete Confirmation Dialog */}
      <ConfirmDialog
        isOpen={!!itemToDelete}
        onClose={() => setItemToDelete(null)}
        onConfirm={handleDeleteConfirm}
        title={`Delete ${itemToDelete?.type === 'category' ? 'Category' : 'Audience'}?`}
        message={`Are you sure you want to delete "${itemToDelete?.name}"? This action cannot be undone.`}
        confirmText="Delete"
        variant="danger"
        isLoading={deleteCategory.isPending || deleteAudience.isPending}
      />
    </div>
  );
}

// Category Detail Component
function CategoryDetail({
  category,
  onEdit,
  onDelete,
  onToggleActive,
}: {
  category: SearchCategory;
  onEdit: () => void;
  onDelete: () => void;
  onToggleActive: (isActive: boolean) => void;
}) {
  return (
    <div className="p-6">
      <div className="flex items-start justify-between mb-6">
        <div className="flex items-center gap-3">
          {category.icon && <span className="text-3xl">{category.icon}</span>}
          <div>
            <h2 className="text-2xl font-bold text-foreground">{category.displayName}</h2>
            <p className="text-sm text-muted-foreground">Category: {category.name}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => onToggleActive(!category.isActive)}
            className={`p-2 rounded-lg transition-colors ${
              category.isActive
                ? 'bg-success-100 text-success-700 hover:bg-success-200'
                : 'bg-muted text-muted-foreground hover:bg-muted/80'
            }`}
            title={category.isActive ? 'Deactivate' : 'Activate'}
          >
            {category.isActive ? <Power className="w-4 h-4" /> : <PowerOff className="w-4 h-4" />}
          </button>
          <button
            onClick={onEdit}
            className="p-2 bg-muted text-foreground rounded-lg hover:bg-muted/80 transition-colors"
            title="Edit"
          >
            <Edit2 className="w-4 h-4" />
          </button>
          <button
            onClick={onDelete}
            className="p-2 bg-danger-100 text-danger-700 rounded-lg hover:bg-danger-200 transition-colors"
            title="Delete"
          >
            <Trash2 className="w-4 h-4" />
          </button>
        </div>
      </div>

      <div className="space-y-6">
        <div>
          <h3 className="text-sm font-medium text-muted-foreground uppercase mb-2">Description</h3>
          <p className="text-foreground">{category.description}</p>
        </div>

        <div className="grid grid-cols-3 gap-4">
          <div className="bg-card p-4 rounded-lg border border-border">
            <p className="text-sm text-muted-foreground">Priority</p>
            <p className="text-2xl font-bold text-foreground mt-1">{category.priority}</p>
          </div>
          <div className="bg-card p-4 rounded-lg border border-border">
            <p className="text-sm text-muted-foreground">Default Alpha</p>
            <p className="text-2xl font-bold text-foreground mt-1">{category.defaultAlpha}</p>
          </div>
          <div className="bg-card p-4 rounded-lg border border-border">
            <p className="text-sm text-muted-foreground">Status</p>
            <p className={`text-lg font-semibold mt-1 ${category.isActive ? 'text-success-600' : 'text-muted-foreground'}`}>
              {category.isActive ? 'Active' : 'Inactive'}
            </p>
          </div>
        </div>

        <div>
          <h3 className="text-sm font-medium text-muted-foreground uppercase mb-2">Path Patterns</h3>
          <div className="space-y-2">
            {category.pathPatterns.map((pattern, index) => (
              <div key={index} className="bg-card px-3 py-2 rounded-lg border border-border font-mono text-sm">
                {pattern}
              </div>
            ))}
          </div>
        </div>

        {category.color && (
          <div>
            <h3 className="text-sm font-medium text-muted-foreground uppercase mb-2">Color</h3>
            <div className="flex items-center gap-2">
              <div
                className="w-8 h-8 rounded border border-border"
                style={{ backgroundColor: category.color }}
              />
              <span className="text-sm font-mono text-foreground">{category.color}</span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

// Audience Detail Component
function AudienceDetail({
  audience,
  categories,
  onEdit,
  onDelete,
  onToggleActive,
}: {
  audience: SearchAudience;
  categories: SearchCategory[];
  onEdit: () => void;
  onDelete: () => void;
  onToggleActive: (isActive: boolean) => void;
}) {
  const audienceCategories = categories.filter((c) => audience.categoryNames.includes(c.name));

  return (
    <div className="p-6">
      <div className="flex items-start justify-between mb-6">
        <div className="flex items-center gap-3">
          {audience.icon && <span className="text-3xl">{audience.icon}</span>}
          <div>
            <h2 className="text-2xl font-bold text-foreground">{audience.displayName}</h2>
            <p className="text-sm text-muted-foreground">Audience: {audience.name}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => onToggleActive(!audience.isActive)}
            className={`p-2 rounded-lg transition-colors ${
              audience.isActive
                ? 'bg-success-100 text-success-700 hover:bg-success-200'
                : 'bg-muted text-muted-foreground hover:bg-muted/80'
            }`}
            title={audience.isActive ? 'Deactivate' : 'Activate'}
          >
            {audience.isActive ? <Power className="w-4 h-4" /> : <PowerOff className="w-4 h-4" />}
          </button>
          <button
            onClick={onEdit}
            className="p-2 bg-muted text-foreground rounded-lg hover:bg-muted/80 transition-colors"
            title="Edit"
          >
            <Edit2 className="w-4 h-4" />
          </button>
          <button
            onClick={onDelete}
            className="p-2 bg-danger-100 text-danger-700 rounded-lg hover:bg-danger-200 transition-colors"
            title="Delete"
          >
            <Trash2 className="w-4 h-4" />
          </button>
        </div>
      </div>

      <div className="space-y-6">
        <div>
          <h3 className="text-sm font-medium text-muted-foreground uppercase mb-2">Description</h3>
          <p className="text-foreground">{audience.description}</p>
        </div>

        <div className="grid grid-cols-3 gap-4">
          <div className="bg-card p-4 rounded-lg border border-border">
            <p className="text-sm text-muted-foreground">Default Alpha</p>
            <p className="text-2xl font-bold text-foreground mt-1">{audience.defaultAlpha}</p>
          </div>
          <div className="bg-card p-4 rounded-lg border border-border">
            <p className="text-sm text-muted-foreground">Max Tokens</p>
            <p className="text-2xl font-bold text-foreground mt-1">{audience.maxTokens.toLocaleString()}</p>
          </div>
          <div className="bg-card p-4 rounded-lg border border-border">
            <p className="text-sm text-muted-foreground">Status</p>
            <p className={`text-lg font-semibold mt-1 ${audience.isActive ? 'text-success-600' : 'text-muted-foreground'}`}>
              {audience.isActive ? 'Active' : 'Inactive'}
            </p>
          </div>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div className="bg-card p-4 rounded-lg border border-border">
            <p className="text-sm text-muted-foreground mb-2">Include Reasoning</p>
            <p className={`text-lg font-semibold ${audience.includeReasoning ? 'text-success-600' : 'text-muted-foreground'}`}>
              {audience.includeReasoning ? 'Yes' : 'No'}
            </p>
          </div>
          <div className="bg-card p-4 rounded-lg border border-border">
            <p className="text-sm text-muted-foreground mb-2">Include Insights</p>
            <p className={`text-lg font-semibold ${audience.includeInsights ? 'text-success-600' : 'text-muted-foreground'}`}>
              {audience.includeInsights ? 'Yes' : 'No'}
            </p>
          </div>
        </div>

        <div>
          <h3 className="text-sm font-medium text-muted-foreground uppercase mb-2">Categories ({audienceCategories.length})</h3>
          {audienceCategories.length > 0 ? (
            <div className="grid grid-cols-2 gap-2">
              {audienceCategories.map((category) => (
                <div key={category.id} className="bg-card px-3 py-2 rounded-lg border border-border flex items-center gap-2">
                  {category.icon && <span>{category.icon}</span>}
                  <span className="text-sm text-foreground">{category.displayName}</span>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">No categories assigned</p>
          )}
        </div>
      </div>
    </div>
  );
}
