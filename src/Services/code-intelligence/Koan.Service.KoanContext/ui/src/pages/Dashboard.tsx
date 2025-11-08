export default function Dashboard() {
  return (
    <div className="min-h-screen bg-background p-8">
      <div className="max-w-7xl mx-auto">
        <h1 className="text-4xl font-bold text-foreground mb-2">
          Koan.Context Dashboard
        </h1>
        <p className="text-muted-foreground mb-8">
          Code Intelligence Platform
        </p>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-xl font-semibold mb-2">Welcome</h2>
            <p className="text-muted-foreground">
              React + TypeScript + Tailwind is now configured and running!
            </p>
          </div>

          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-xl font-semibold mb-2">Status</h2>
            <p className="text-success-600 dark:text-success-400">
              ✓ Single-server architecture
            </p>
            <p className="text-success-600 dark:text-success-400">
              ✓ Deep linking enabled
            </p>
            <p className="text-success-600 dark:text-success-400">
              ✓ Design tokens ported
            </p>
          </div>

          <div className="bg-card border border-border rounded-lg p-6">
            <h2 className="text-xl font-semibold mb-2">Next Steps</h2>
            <p className="text-muted-foreground text-sm">
              1. Install dependencies (npm ci)<br />
              2. Build UI (npm run build)<br />
              3. Run service (dotnet run)
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
