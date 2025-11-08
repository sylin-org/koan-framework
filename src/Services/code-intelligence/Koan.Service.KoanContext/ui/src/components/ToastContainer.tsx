import { useToastStore, type Toast } from '@/stores/toastStore';
import { CheckCircle, XCircle, AlertTriangle, Info, X } from 'lucide-react';

export default function ToastContainer() {
  const toasts = useToastStore((state) => state.toasts);
  const removeToast = useToastStore((state) => state.removeToast);

  return (
    <div className="fixed top-4 right-4 z-50 flex flex-col gap-2 max-w-md">
      {toasts.map((toast) => (
        <ToastItem key={toast.id} toast={toast} onClose={() => removeToast(toast.id)} />
      ))}
    </div>
  );
}

interface ToastItemProps {
  toast: Toast;
  onClose: () => void;
}

function ToastItem({ toast, onClose }: ToastItemProps) {
  const { type, title, message } = toast;

  const config = {
    success: {
      icon: CheckCircle,
      bgColor: 'bg-success-50',
      borderColor: 'border-success-200',
      iconColor: 'text-success-600',
      titleColor: 'text-success-900',
      messageColor: 'text-success-700',
    },
    error: {
      icon: XCircle,
      bgColor: 'bg-danger-50',
      borderColor: 'border-danger-200',
      iconColor: 'text-danger-600',
      titleColor: 'text-danger-900',
      messageColor: 'text-danger-700',
    },
    warning: {
      icon: AlertTriangle,
      bgColor: 'bg-yellow-50',
      borderColor: 'border-yellow-200',
      iconColor: 'text-yellow-600',
      titleColor: 'text-yellow-900',
      messageColor: 'text-yellow-700',
    },
    info: {
      icon: Info,
      bgColor: 'bg-primary-50',
      borderColor: 'border-primary-200',
      iconColor: 'text-primary-600',
      titleColor: 'text-primary-900',
      messageColor: 'text-primary-700',
    },
  };

  const { icon: Icon, bgColor, borderColor, iconColor, titleColor, messageColor } = config[type];

  return (
    <div
      className={`${bgColor} ${borderColor} border rounded-lg p-4 shadow-lg animate-in slide-in-from-right-full duration-300 min-w-[320px] max-w-md`}
      role="alert"
    >
      <div className="flex items-start gap-3">
        <Icon className={`w-5 h-5 ${iconColor} mt-0.5 flex-shrink-0`} />
        <div className="flex-1 min-w-0">
          <p className={`font-medium ${titleColor}`}>{title}</p>
          {message && <p className={`text-sm ${messageColor} mt-1`}>{message}</p>}
        </div>
        <button
          onClick={onClose}
          className={`${iconColor} hover:opacity-70 transition-opacity flex-shrink-0`}
          aria-label="Close notification"
        >
          <X className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}
