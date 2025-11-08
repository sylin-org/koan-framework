/**
 * Reusable skeleton loading components
 */

interface SkeletonProps {
  className?: string;
}

export function Skeleton({ className = '' }: SkeletonProps) {
  return (
    <div
      className={`animate-pulse bg-muted rounded ${className}`}
      aria-label="Loading..."
      role="status"
    />
  );
}

export function ProjectCardSkeleton() {
  return (
    <div className="bg-card border border-border rounded-lg p-6 animate-pulse">
      <div className="flex items-start justify-between mb-4">
        <div className="flex-1">
          <Skeleton className="h-6 w-48 mb-2" />
          <Skeleton className="h-4 w-64" />
        </div>
        <Skeleton className="h-10 w-24" />
      </div>
      <div className="grid grid-cols-3 gap-4 mt-4">
        <div>
          <Skeleton className="h-4 w-20 mb-2" />
          <Skeleton className="h-6 w-16" />
        </div>
        <div>
          <Skeleton className="h-4 w-20 mb-2" />
          <Skeleton className="h-6 w-16" />
        </div>
        <div>
          <Skeleton className="h-4 w-20 mb-2" />
          <Skeleton className="h-6 w-16" />
        </div>
      </div>
    </div>
  );
}

export function JobCardSkeleton() {
  return (
    <div className="bg-card border border-border rounded-lg p-5 animate-pulse">
      <div className="flex items-start justify-between mb-3">
        <div className="flex items-start gap-3 flex-1">
          <Skeleton className="w-10 h-10 rounded-full" />
          <div className="flex-1">
            <Skeleton className="h-5 w-48 mb-2" />
            <Skeleton className="h-4 w-32" />
          </div>
        </div>
        <Skeleton className="h-6 w-20 rounded-full" />
      </div>
      <div className="bg-muted/30 rounded-lg p-3 mb-3">
        <Skeleton className="h-3 w-full mb-2" />
        <Skeleton className="h-2 w-3/4" />
      </div>
      <div className="grid grid-cols-4 gap-4">
        {[1, 2, 3, 4].map((i) => (
          <div key={i}>
            <Skeleton className="h-3 w-16 mb-1" />
            <Skeleton className="h-4 w-12" />
          </div>
        ))}
      </div>
    </div>
  );
}

export function SearchResultSkeleton() {
  return (
    <div className="bg-card border border-border rounded-lg p-5 animate-pulse">
      <div className="flex items-start justify-between mb-3">
        <div className="flex items-start gap-3 flex-1">
          <Skeleton className="w-5 h-5 rounded" />
          <div className="flex-1">
            <Skeleton className="h-5 w-64 mb-2" />
            <Skeleton className="h-4 w-48 mb-1" />
            <Skeleton className="h-3 w-32" />
          </div>
        </div>
        <div className="flex gap-2">
          <Skeleton className="w-8 h-8 rounded" />
          <Skeleton className="w-8 h-8 rounded" />
        </div>
      </div>
      <div className="bg-muted/30 rounded-lg p-4">
        <Skeleton className="h-4 w-full mb-2" />
        <Skeleton className="h-4 w-11/12 mb-2" />
        <Skeleton className="h-4 w-10/12 mb-2" />
        <Skeleton className="h-4 w-9/12" />
      </div>
    </div>
  );
}

export function MetricCardSkeleton() {
  return (
    <div className="bg-card border border-border rounded-lg p-6 animate-pulse">
      <div className="flex items-center justify-between mb-2">
        <Skeleton className="h-4 w-24" />
        <Skeleton className="w-10 h-10 rounded-full" />
      </div>
      <Skeleton className="h-8 w-20 mb-1" />
      <Skeleton className="h-3 w-32" />
    </div>
  );
}

export function TableRowSkeleton() {
  return (
    <tr className="border-b border-border">
      <td className="px-6 py-4">
        <Skeleton className="h-5 w-48" />
      </td>
      <td className="px-6 py-4">
        <Skeleton className="h-5 w-24" />
      </td>
      <td className="px-6 py-4">
        <Skeleton className="h-5 w-32" />
      </td>
      <td className="px-6 py-4">
        <Skeleton className="h-5 w-20" />
      </td>
      <td className="px-6 py-4">
        <div className="flex gap-2">
          <Skeleton className="h-8 w-16 rounded" />
          <Skeleton className="h-8 w-16 rounded" />
        </div>
      </td>
    </tr>
  );
}

export function JobDetailSkeleton() {
  return (
    <div className="min-h-screen bg-background p-8">
      <div className="max-w-6xl mx-auto">
        <div className="mb-6">
          <Skeleton className="h-8 w-64 mb-2" />
          <Skeleton className="h-5 w-96" />
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-6">
          {[1, 2, 3].map((i) => (
            <div key={i} className="bg-card border border-border rounded-lg p-6">
              <Skeleton className="h-5 w-24 mb-4" />
              <Skeleton className="h-3 w-full mb-2" />
              <Skeleton className="h-2 w-3/4" />
            </div>
          ))}
        </div>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="bg-card border border-border rounded-lg p-4 text-center">
              <Skeleton className="w-12 h-12 rounded-full mx-auto mb-2" />
              <Skeleton className="h-6 w-16 mx-auto mb-1" />
              <Skeleton className="h-4 w-20 mx-auto" />
            </div>
          ))}
        </div>

        <div className="bg-card border border-border rounded-lg p-6">
          <Skeleton className="h-6 w-32 mb-4" />
          <div className="space-y-4">
            {[1, 2, 3, 4].map((i) => (
              <div key={i} className="flex gap-4">
                <Skeleton className="w-2 h-2 rounded-full mt-2" />
                <div className="flex-1">
                  <Skeleton className="h-5 w-48 mb-2" />
                  <Skeleton className="h-4 w-32" />
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

export function DashboardSkeleton() {
  return (
    <div className="min-h-screen bg-background p-8">
      <div className="max-w-7xl mx-auto">
        <Skeleton className="h-10 w-64 mb-8" />

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
          {[1, 2, 3, 4].map((i) => (
            <MetricCardSkeleton key={i} />
          ))}
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <div className="bg-card border border-border rounded-lg p-6">
            <Skeleton className="h-6 w-40 mb-4" />
            <div className="space-y-3">
              {[1, 2, 3].map((i) => (
                <JobCardSkeleton key={i} />
              ))}
            </div>
          </div>

          <div className="bg-card border border-border rounded-lg p-6">
            <Skeleton className="h-6 w-40 mb-4" />
            <div className="space-y-3">
              {[1, 2, 3].map((i) => (
                <ProjectCardSkeleton key={i} />
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
