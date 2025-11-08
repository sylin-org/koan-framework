import { useToastStore, type ToastType } from '@/stores/toastStore';

interface ToastOptions {
  title: string;
  message?: string;
  duration?: number;
}

/**
 * Hook to show toast notifications
 */
export function useToast() {
  const addToast = useToastStore((state) => state.addToast);

  const show = (type: ToastType, options: ToastOptions) => {
    addToast({
      type,
      ...options,
    });
  };

  return {
    success: (title: string, message?: string, duration?: number) =>
      show('success', { title, message, duration }),

    error: (title: string, message?: string, duration?: number) =>
      show('error', { title, message, duration }),

    warning: (title: string, message?: string, duration?: number) =>
      show('warning', { title, message, duration }),

    info: (title: string, message?: string, duration?: number) =>
      show('info', { title, message, duration }),
  };
}
