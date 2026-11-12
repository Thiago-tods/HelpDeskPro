import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { DefinitionList, PageHeader, PriorityBadge, SlaBadge, StatusBadge } from '@/components/common';
import { Button } from '@/components/ui/button';
import { Card, CardBody, CardHeader } from '@/components/ui/card';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { TabPanel, Tabs } from '@/components/ui/tabs';
import { AttachmentsTab } from '@/features/tickets/detail/AttachmentsTab';
import { ConversationTab } from '@/features/tickets/detail/ConversationTab';
import { EditTicketModal } from '@/features/tickets/detail/EditTicketModal';
import { HistoryTab } from '@/features/tickets/detail/HistoryTab';
import { TicketActions } from '@/features/tickets/detail/TicketActions';
import { useAttachments, useMessages, useTicket } from '@/features/tickets/api';
import { ApiError } from '@/lib/api';
import { formatDateTime } from '@/lib/format';
import type { TicketDetail } from '@/types/api';

type TabId = 'conversation' | 'attachments' | 'history';

function SlaSummary({ ticket }: { ticket: TicketDetail }) {
  const { sla } = ticket;
  return (
    <ul className="flex flex-col gap-2 text-[13px]">
      <li>
        <span className="text-muted">1ª resposta até </span>
        <strong>{formatDateTime(sla.firstResponseDueAt)}</strong>
        <span className="block text-xs text-muted">
          {sla.firstRespondedAt ? `Respondido em ${formatDateTime(sla.firstRespondedAt)}` : 'Ainda sem resposta da equipe'}
          {sla.firstResponseBreachedAt ? ' • prazo violado' : ''}
        </span>
      </li>
      <li>
        <span className="text-muted">Resolução até </span>
        <strong>{formatDateTime(sla.resolutionDueAt)}</strong>
        <span className="block text-xs text-muted">
          {ticket.resolvedAt ? `Resolvido em ${formatDateTime(ticket.resolvedAt)}` : 'Em aberto'}
          {sla.resolutionBreachedAt ? ' • prazo violado' : ''}
        </span>
      </li>
    </ul>
  );
}

export default function TicketDetailPage() {
  const { id = '' } = useParams();
  const ticket = useTicket(id);
  const messages = useMessages(id);
  const attachments = useAttachments(id);
  const [tab, setTab] = useState<TabId>('conversation');
  const [editing, setEditing] = useState(false);

  if (ticket.isPending) {
    return (
      <div className="flex flex-col gap-4" role="status" aria-label="Carregando chamado">
        <Skeleton className="h-8 w-2/3" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (ticket.isError) {
    const notFound = ticket.error instanceof ApiError && ticket.error.status === 404;
    return (
      <Card>
        {notFound ? (
          <EmptyState title="Chamado não encontrado" description="Ele não existe ou você não tem acesso a ele." action={<Link to="/tickets"><Button><ArrowLeft className="size-4" aria-hidden /> Voltar aos chamados</Button></Link>} />
        ) : (
          <ErrorState error={ticket.error} onRetry={() => void ticket.refetch()} />
        )}
      </Card>
    );
  }

  const t = ticket.data;

  return (
    <>
      <PageHeader
        title={t.title}
        description={`${t.ticketNumber} • aberto em ${formatDateTime(t.createdAt)}`}
        actions={
          <>
            <StatusBadge status={t.status} />
            <PriorityBadge name={t.priorityName} level={t.priorityLevel} />
            <SlaBadge state={t.slaState} />
          </>
        }
      />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="flex min-w-0 flex-col gap-6">
          <Card>
            <CardHeader title="Descrição" />
            <CardBody>
              <p className="text-[13px] break-words whitespace-pre-wrap">{t.description}</p>
            </CardBody>
          </Card>

          <Card>
            <div className="px-2">
              <Tabs
                idPrefix="ticket"
                label="Seções do chamado"
                value={tab}
                onChange={(v) => setTab(v as TabId)}
                tabs={[
                  { id: 'conversation', label: 'Conversa', count: messages.data?.length },
                  { id: 'attachments', label: 'Anexos', count: attachments.data?.length },
                  { id: 'history', label: 'Histórico' },
                ]}
              />
            </div>
            <CardBody className="pt-0">
              <TabPanel idPrefix="ticket" id="conversation" active={tab === 'conversation'}><ConversationTab ticket={t} /></TabPanel>
              <TabPanel idPrefix="ticket" id="attachments" active={tab === 'attachments'}><AttachmentsTab ticket={t} /></TabPanel>
              <TabPanel idPrefix="ticket" id="history" active={tab === 'history'}><HistoryTab ticketId={t.id} /></TabPanel>
            </CardBody>
          </Card>
        </div>

        <aside aria-label="Resumo e ações" className="flex flex-col gap-6">
          <TicketActions ticket={t} onEdit={() => setEditing(true)} />

          <Card>
            <CardHeader title="Detalhes" />
            <CardBody className="flex flex-col gap-5">
              <DefinitionList
                items={[
                  { label: 'Solicitante', value: t.requesterName },
                  { label: 'Responsável', value: t.assignedToName ?? <span className="text-muted">Livre</span> },
                  { label: 'Equipe', value: t.teamName ?? '—' },
                  { label: 'Categoria', value: t.categoryName },
                  { label: 'Atualizado em', value: formatDateTime(t.updatedAt) },
                  { label: 'Fechado em', value: formatDateTime(t.closedAt) },
                ]}
              />
              <div className="border-t border-border pt-4">
                <h3 className="mb-2 text-[13px] font-semibold">Prazos de SLA</h3>
                <SlaSummary ticket={t} />
              </div>
            </CardBody>
          </Card>
        </aside>
      </div>

      {editing && <EditTicketModal ticket={t} open onOpenChange={setEditing} />}
    </>
  );
}
