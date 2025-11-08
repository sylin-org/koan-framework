import { ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import {
  Search,
  LayoutDashboard,
  FolderOpen,
  Activity,
  Settings,
  BookOpen,
  MessageCircle,
} from 'lucide-react';
import { clsx } from 'clsx';

interface LayoutProps {
  children: ReactNode;
}

interface NavItem {
  icon: typeof Search;
  label: string;
  path: string;
  primary?: boolean;
}

const primaryNav: NavItem[] = [
  { icon: Search, label: 'Search', path: '/', primary: true },
  { icon: LayoutDashboard, label: 'Dashboard', path: '/dashboard' },
  { icon: FolderOpen, label: 'Projects', path: '/projects' },
  { icon: Activity, label: 'Jobs', path: '/jobs' },
  { icon: Settings, label: 'Settings', path: '/settings' },
];

const secondaryNav: NavItem[] = [
  { icon: BookOpen, label: 'Docs', path: '/docs' },
  { icon: MessageCircle, label: 'Support', path: '/support' },
];

export default function Layout({ children }: LayoutProps) {
  const location = useLocation();

  const isActive = (path: string) => {
    if (path === '/') {
      return location.pathname === '/';
    }
    return location.pathname.startsWith(path);
  };

  return (
    <div className="flex h-screen bg-background">
      {/* Left Sidebar */}
      <aside className="w-64 bg-card border-r border-border flex flex-col">
        {/* Logo */}
        <div className="p-6 border-b border-border">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 bg-primary-600 rounded-lg flex items-center justify-center">
              <span className="text-white font-bold text-sm">K</span>
            </div>
            <div>
              <h1 className="text-lg font-bold text-foreground">Koan.Context</h1>
              <p className="text-xs text-muted-foreground">v0.6.3</p>
            </div>
          </div>
        </div>

        {/* Primary Navigation */}
        <nav className="flex-1 p-4 space-y-1">
          {primaryNav.map((item) => {
            const Icon = item.icon;
            const active = isActive(item.path);

            return (
              <Link
                key={item.path}
                to={item.path}
                className={clsx(
                  'flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors',
                  active
                    ? 'bg-primary-50 text-primary-700 font-medium'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground'
                )}
              >
                <Icon className="w-5 h-5" />
                <span>{item.label}</span>
              </Link>
            );
          })}
        </nav>

        {/* Secondary Navigation */}
        <div className="p-4 border-t border-border space-y-1">
          {secondaryNav.map((item) => {
            const Icon = item.icon;
            const active = isActive(item.path);

            return (
              <Link
                key={item.path}
                to={item.path}
                className={clsx(
                  'flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors text-sm',
                  active
                    ? 'bg-primary-50 text-primary-700 font-medium'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground'
                )}
              >
                <Icon className="w-4 h-4" />
                <span>{item.label}</span>
              </Link>
            );
          })}
        </div>
      </aside>

      {/* Main Content */}
      <main className="flex-1 overflow-auto">
        {children}
      </main>
    </div>
  );
}
