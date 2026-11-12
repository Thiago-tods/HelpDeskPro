import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { AlertCircle, CheckCircle2, Info, X } from 'lucide-react';
import { cn } from '@/lib/cn';

type ToastKind = 'success' | 'error' | 'info';

interface ToastItem {
  id: number;
  kind: ToastKind;
  message: string;
}

interface ToastApi {
  success: (message: string) => void;
  error: (message: string) => void;
  info: (message: string) => void;
}

const ToastContext = createContext<ToastApi | null>(null);
let nextId = 1;

const styles: Record<ToastKind, string> = {
  success: 'border-success/40',
  error: 'border-danger/50',
  info: 'border-info/40',
};

const icons = {
  success: <CheckCircle2 className="size-5 shrink-0 text-success" aria-hidden />,
  error: <AlertCircle className="size-5 shrink-0 text-danger" aria-hidden />,
  info: <Info className="size-5 shrink-0 text-info" aria-hidden />,
};

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);

  const dismiss = useCallback((id: number) => setItems((current) => current.filter((t) => t.id !== id)), []);

  const push = useCallback(
    (kind: ToastKind, message: string) => {
      const id = nextId++;
      setItems((current) => [...current.slice(-3), { id, kind, message }]);
      setTimeout(() => dismiss(id), kind === 'error' ? 8000 : 5000);
    },
    [dismiss],
  );

  const api = useMemo<ToastApi>(
    () => ({
      success: (m) => push('success', m),
      error: (m) => push('error', m),
      info: (m) => push('info', m),
    }),
    [push],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div role="region" aria-label="Notificações" aria-live="polite" className="pointer-events-none fixed right-4 bottom-4 z-[60] flex w-[min(24rem,calc(100vw-2rem))] flex-col gap-2">
        {items.map((t) => (
          <div key={t.id} role={t.kind === 'error' ? 'alert' : 'status'} className={cn('fade-in pointer-events-auto flex items-start gap-3 rounded-lg border bg-surface p-3.5 shadow-lg', styles[t.kind])}>
            {icons[t.kind]}
            <p className="flex-1 text-[13px]">{t.message}</p>
            <button type="button" aria-label="Dispensar notificação" onClick={() => dismiss(t.id)} className="rounded p-0.5 text-muted hover:bg-surface-2">
              <X className="size-4" aria-hidden />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast deve ser usado dentro de ToastProvider');
  return ctx;
}
