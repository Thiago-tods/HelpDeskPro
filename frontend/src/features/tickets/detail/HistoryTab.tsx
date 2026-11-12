import { AlertTriangle, CheckCircle2, Circle, Flag, RotateCcw, UserCheck, Users, XCircle } from 'lucide-react';
import type { ReactNode } from 'react';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { cn } from '@/lib/cn';
import { formatDateTime, historyDescription } from '@/lib/format';
import type { HistoryAction } from '@/types/api';
import { useHistory } from '../api';

const icons: Record<HistoryAction, { icon: ReactNode; tone: string }> = {
  CREATED: { icon: <Circle className="size-4" />, tone: 'bg-info-soft text-info' },
  STATUS_CHANGED: { icon: <RotateCcw className="size-4" />, tone: 'bg-accent-soft text-accent-text' },
  PRIORITY_CHANGED: { icon: <Flag className="size-4" />, tone: 'bg-warning-soft text-warning' },
  ASSIGNED: { icon: <UserCheck className="size-4" />, tone: 'bg-accent-soft text-accent-text' },
  TEAM_CHANGED: { icon: <Users className="size-4" />, tone: 'bg-accent-soft text-accent-text' },
  RESOLVED: { icon: <CheckCircle2 className="size-4" />, tone: 'bg-success-soft text-success' },
  REOPENED: { icon: <RotateCcw className="size-4" />, tone: 'bg-warning-soft text-warning' },
  CLOSED: { icon: <CheckCircle2 className="size-4" />, tone: 'bg-neutral-soft text-muted' },
  CANCELLED: { icon: <XCircle className="size-4" />, tone: 'bg-neutral-soft text-muted' },
  SLA_BREACHED: { icon: <AlertTriangle className="size-4" />, tone: 'bg-danger-soft text-danger' },
};

export function HistoryTab({ ticketId }: { ticketId: string }) {
  const history = useHistory(ticketId);

  if (history.isPending) return <SkeletonRows rows={4} />;
  if (history.isError) return <ErrorState error={history.error} onRetry={() => void history.refetch()} />;
  if (history.data.length === 0) return <EmptyState title="Sem histórico" />;

  return (
    <ol aria-label="Histórico do chamado" className="relative flex flex-col gap-5 before:absolute before:top-2 before:bottom-2 before:left-4 before:w-px before:bg-border">
      {history.data.map((entry) => (
        <li key={entry.id} className="relative flex gap-4">
          <span aria-hidden className={cn('z-10 grid size-8 shrink-0 place-items-center rounded-full ring-4 ring-surface', icons[entry.action].tone)}>
            {icons[entry.action].icon}
          </span>
          <div className="min-w-0 pt-0.5">
            <p className="text-[13px] font-medium">{historyDescription(entry)}</p>
            <p className="text-xs text-muted">
              {entry.actorName} • <time dateTime={entry.createdAt}>{formatDateTime(entry.createdAt)}</time>
            </p>
          </div>
        </li>
      ))}
    </ol>
  );
}
