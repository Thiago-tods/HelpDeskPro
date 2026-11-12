import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { cn } from '@/lib/cn';
import { initials, ROLE_LABEL, SLA_LABEL, STATUS_LABEL } from '@/lib/format';
import type { Role, SlaState, TicketStatus } from '@/types/api';
import { Badge, type Tone } from './ui/badge';

const statusTone: Record<TicketStatus, Tone> = {
  NEW: 'info',
  IN_PROGRESS: 'accent',
  WAITING_FOR_USER: 'warning',
  RESOLVED: 'success',
  CLOSED: 'neutral',
  CANCELLED: 'neutral',
};

export function StatusBadge({ status }: { status: TicketStatus }) {
  return <Badge tone={statusTone[status]}>{STATUS_LABEL[status]}</Badge>;
}

const slaTone: Record<SlaState, Tone> = {
  ON_TRACK: 'success',
  AT_RISK: 'warning',
  OVERDUE: 'danger',
  MET: 'success',
  MISSED: 'danger',
  NOT_APPLICABLE: 'neutral',
};

/** Estado de SLA vindo do backend. Estados sem relevância visual (não se aplica) não são exibidos. */
export function SlaBadge({ state }: { state: SlaState }) {
  if (state === 'NOT_APPLICABLE') return null;
  return <Badge tone={slaTone[state]}>{SLA_LABEL[state]}</Badge>;
}

const priorityTone = (level: number): Tone => (level >= 4 ? 'danger' : level === 3 ? 'warning' : level === 2 ? 'info' : 'neutral');

export function PriorityBadge({ name, level }: { name: string; level: number }) {
  return <Badge tone={priorityTone(level)}>{name}</Badge>;
}

export function RoleBadge({ role }: { role: Role }) {
  return <Badge tone={role === 'ADMIN' ? 'accent' : role === 'AGENT' ? 'info' : 'neutral'}>{ROLE_LABEL[role]}</Badge>;
}

export function Avatar({ name, className }: { name: string; className?: string }) {
  return (
    <span aria-hidden className={cn('grid size-8 shrink-0 place-items-center rounded-full bg-accent-soft text-xs font-semibold text-accent-text', className)}>
      {initials(name)}
    </span>
  );
}

export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return (
    <header className="mb-6 flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {description && <p className="mt-1 text-[13px] text-muted">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </header>
  );
}

const statTone: Record<string, string> = {
  default: 'bg-accent-soft text-accent-text',
  info: 'bg-info-soft text-info',
  success: 'bg-success-soft text-success',
  warning: 'bg-warning-soft text-warning',
  danger: 'bg-danger-soft text-danger',
};

interface StatCardProps {
  label: string;
  value: ReactNode;
  icon: ReactNode;
  tone?: keyof typeof statTone;
  hint?: string;
  to?: string;
}

export function StatCard({ label, value, icon, tone = 'default', hint, to }: StatCardProps) {
  const body = (
    <div className={cn('flex items-center gap-4 rounded-xl border border-border bg-surface p-4', to && 'transition-colors hover:border-accent')}>
      <div className={cn('grid size-11 shrink-0 place-items-center rounded-xl', statTone[tone])} aria-hidden>
        {icon}
      </div>
      <div className="min-w-0">
        <p className="truncate text-[13px] text-muted">{label}</p>
        <div className="text-2xl leading-tight font-semibold">{value}</div>
        {hint && <p className="truncate text-xs text-muted">{hint}</p>}
      </div>
    </div>
  );
  return to ? (
    <Link to={to} className="block rounded-xl">
      {body}
    </Link>
  ) : (
    body
  );
}

export function DefinitionList({ items }: { items: { label: string; value: ReactNode }[] }) {
  return (
    <dl className="grid grid-cols-1 gap-x-6 gap-y-3 sm:grid-cols-2">
      {items.map((item) => (
        <div key={item.label}>
          <dt className="text-xs text-muted">{item.label}</dt>
          <dd className="mt-0.5 text-[13px] font-medium">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}
