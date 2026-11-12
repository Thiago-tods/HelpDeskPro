import type { ReactNode } from 'react';
import { AlertTriangle, Inbox, Loader2 } from 'lucide-react';
import { cn } from '@/lib/cn';
import { ApiError, messageOf } from '@/lib/api';
import { Button } from './button';

export function Skeleton({ className }: { className?: string }) {
  return <div aria-hidden className={cn('skeleton rounded-md', className)} />;
}

export function SkeletonRows({ rows = 5 }: { rows?: number }) {
  return (
    <div role="status" aria-label="Carregando" className="flex flex-col gap-3 p-5">
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} className="h-9 w-full" />
      ))}
      <span className="sr-only">Carregando…</span>
    </div>
  );
}

export function PageLoader({ label = 'Carregando…' }: { label?: string }) {
  return (
    <div role="status" className="grid min-h-[40vh] place-items-center text-muted">
      <div className="flex items-center gap-2.5">
        <Loader2 className="size-5 animate-spin" aria-hidden />
        <span>{label}</span>
      </div>
    </div>
  );
}

export function EmptyState({ icon, title, description, action }: { icon?: ReactNode; title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 px-6 py-14 text-center">
      <div className="mb-1 grid size-12 place-items-center rounded-full bg-surface-2 text-muted">{icon ?? <Inbox className="size-6" aria-hidden />}</div>
      <h3 className="text-[15px] font-semibold">{title}</h3>
      {description && <p className="max-w-sm text-[13px] text-muted">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const forbidden = error instanceof ApiError && error.status === 403;
  return (
    <div role="alert" className="flex flex-col items-center gap-2 px-6 py-12 text-center">
      <div className="mb-1 grid size-12 place-items-center rounded-full bg-danger-soft text-danger">
        <AlertTriangle className="size-6" aria-hidden />
      </div>
      <h3 className="text-[15px] font-semibold">{forbidden ? 'Acesso negado' : 'Não foi possível carregar'}</h3>
      <p className="max-w-sm text-[13px] text-muted">{messageOf(error)}</p>
      {onRetry && (
        <Button variant="secondary" size="sm" className="mt-2" onClick={onRetry}>
          Tentar novamente
        </Button>
      )}
    </div>
  );
}
