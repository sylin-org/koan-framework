import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';

/**
 * Global keyboard shortcuts
 * - / : Focus search (navigate to search page)
 * - Esc : Clear/close (context-dependent)
 */
export function useKeyboardShortcuts() {
  const navigate = useNavigate();

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ignore if user is typing in an input/textarea
      const target = e.target as HTMLElement;
      const isTyping = ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);

      // / - Focus search (navigate to search page)
      if (e.key === '/' && !isTyping) {
        e.preventDefault();
        navigate('/');
        // Focus will be handled by SearchPage's autoFocus
      }

      // Esc - Context-dependent close/clear
      if (e.key === 'Escape') {
        // This will be handled by individual components
        // We just dispatch a custom event that components can listen to
        window.dispatchEvent(new CustomEvent('keyboard-escape'));
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [navigate]);
}

/**
 * Hook for components to listen to Escape key
 */
export function useEscapeKey(callback: () => void) {
  useEffect(() => {
    const handleEscape = () => callback();
    window.addEventListener('keyboard-escape', handleEscape);
    return () => window.removeEventListener('keyboard-escape', handleEscape);
  }, [callback]);
}
