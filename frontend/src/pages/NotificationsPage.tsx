import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AlertTriangle, AtSign, Bell, CheckCheck, MessageSquare, RefreshCw, UserCheck } from 'lucide-react';
import type { ReactNode } from 'react';
import { PageHeader } from '@/components/common';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CheckboxField } from '@/components/ui/fields';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { Pagination } from '@/components/ui/table';
import { useMarkAllRead, useMarkRead, useNotifications } from '@/features/insights/api';
import { cn } from '@/lib/cn';
import { formatDateTime, formatRelative, NOTIFICATION_LABEL } from '@/lib/format';
import type { NotificationType } from '@/types/api';

const icons: Record<NotificationType, ReactNode> = {
  NEW_TICKET: <AtSign className="size-5" />,
  TICKET_ASSIGNED: <UserCheck className="size-5" />,
  NEW_MESSAGE: <MessageSquare className="size-5" />,
  STATUS_CHANGED: <RefreshCw className="size-5" />,
  SLA_NEAR_DUE: <AlertTriangle className="size-5" />,
  SLA_BREACHED: <AlertTriangle className="size-5" />,
};

const tones: Record<NotificationType, string> = {
  NEW_TICKET: 'bg-info-soft text-info',
  TICKET_ASSIGNED: 'bg-accent-soft text-accent-text',
  NEW_MESSAGE: 'bg-accent-soft text-accent-text',
  STATUS_CHANGED: 'bg-neutral-soft text-muted',
  SLA_NEAR_DUE: 'bg-warning-soft text-warning',
  SLA_BREACHED: 'bg-danger-soft text-danger',
};

export default function NotificationsPage() {
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const list = useNotifications(page, unreadOnly, 15);
  const markRead = useMarkRead();
  const markAll = useMarkAllRead();

  return (
    <>
      <PageHeader
        title="Notificações"
        description="Avisos sobre seus chamados."
        actions={
          <>
            <CheckboxField label="Somente não lidas" checked={unreadOnly} onChange={(e) => { setUnreadOnly(e.target.checked); setPage(1); }} />
            <Button variant="secondary" loading={markAll.isPending} onClick={() => markAll.mutate()}>
              <CheckCheck className="size-4" aria-hidden /> Marcar todas como lidas
            </Button>
          </>
        }
      />

      <Card>
        {list.isPending ? (
          <SkeletonRows rows={5} />
        ) : list.isError ? (
          <ErrorState error={list.error} onRetry={() => void list.refetch()} />
        ) : list.data.items.length === 0 ? (
          <EmptyState icon={<Bell className="size-6" aria-hidden />} title={unreadOnly ? 'Nenhuma notificação não lida' : 'Sem notificações'} description="Você será avisado aqui sobre mensagens, atribuições, mudanças de status e prazos." />
        ) : (
          <>
            <ul className="divide-y divide-border">
              {list.data.items.map((n) => (
                <li key={n.id}>
                  <button
                    type="button"
                    onClick={() => {
                      if (!n.isRead) markRead.mutate(n.id);
                      if (n.ticketId) navigate(`/tickets/${n.ticketId}`);
                    }}
                    className={cn('flex w-full items-start gap-4 px-5 py-4 text-left transition-colors hover:bg-surface-2/60', !n.isRead && 'bg-accent-soft/30')}
                  >
                    <span aria-hidden className={cn('grid size-10 shrink-0 place-items-center rounded-full', tones[n.type])}>{icons[n.type]}</span>
                    <span className="min-w-0 flex-1">
                      <span className="flex flex-wrap items-center gap-2">
                        <Badge tone={n.type === 'SLA_BREACHED' ? 'danger' : n.type === 'SLA_NEAR_DUE' ? 'warning' : 'neutral'}>{NOTIFICATION_LABEL[n.type]}</Badge>
                        {!n.isRead && <span className="text-xs font-semibold text-accent">Nova</span>}
                      </span>
                      <span className={cn('mt-1 block text-[13px]', !n.isRead && 'font-semibold')}>{n.message}</span>
                      <time dateTime={n.createdAt} title={formatDateTime(n.createdAt)} className="mt-0.5 block text-xs text-muted">{formatRelative(n.createdAt)}</time>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
            <Pagination page={list.data.page} totalPages={list.data.totalPages} totalCount={list.data.totalCount} pageSize={list.data.pageSize} onPageChange={setPage} />
          </>
        )}
      </Card>
    </>
  );
}
