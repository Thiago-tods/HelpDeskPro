import { useMemo, useState, type DragEvent } from 'react';
import { Link } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { HandHelping } from 'lucide-react';
import { useCurrentUser } from '@/app/auth';
import { PageHeader, PriorityBadge, SlaBadge } from '@/components/common';
import { Button } from '@/components/ui/button';
import { CheckboxField } from '@/components/ui/fields';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { moveTicket, useTickets } from '@/features/tickets/api';
import { messageOf } from '@/lib/api';
import { cn } from '@/lib/cn';
import { STATUS_LABEL } from '@/lib/format';
import type { TicketListItem, TicketStatus } from '@/types/api';

const COLUMNS: TicketStatus[] = ['NEW', 'IN_PROGRESS', 'WAITING_FOR_USER', 'RESOLVED'];

export default function KanbanPage() {
  const user = useCurrentUser();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [onlyMine, setOnlyMine] = useState(false);
  const [dragging, setDragging] = useState<string | null>(null);
  const [overColumn, setOverColumn] = useState<TicketStatus | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const tickets = useTickets({
    status: COLUMNS.join(','),
    mine: onlyMine || undefined,
    sortBy: 'priority',
    sortDir: 'desc',
    pageSize: 100,
  });

  const columns = useMemo(() => {
    const grouped: Record<TicketStatus, TicketListItem[]> = { NEW: [], IN_PROGRESS: [], WAITING_FOR_USER: [], RESOLVED: [], CLOSED: [], CANCELLED: [] };
    for (const t of tickets.data?.items ?? []) grouped[t.status].push(t);
    return grouped;
  }, [tickets.data]);

  /**
   * Traduz um movimento do quadro em chamadas à API. As regras reais (permissões e máquina de estados)
   * são do servidor; um movimento negado volta com mensagem e o quadro é recarregado.
   */
  async function move(ticket: TicketListItem, target: TicketStatus) {
    if (ticket.status === target) return;
    if (target === 'NEW') {
      toast.info('Chamados não voltam para "Novo".');
      return;
    }
    setBusy(ticket.id);
    try {
      if (ticket.status === 'NEW' && target === 'IN_PROGRESS') {
        if (user.role !== 'AGENT') {
          toast.info('Atribua um responsável no detalhe do chamado para iniciar o atendimento.');
          return;
        }
        await moveTicket(ticket.id, { assignTo: user.id });
        toast.success(`${ticket.ticketNumber} assumido por você.`);
      } else {
        await moveTicket(ticket.id, { status: target });
        toast.success(`${ticket.ticketNumber} → ${STATUS_LABEL[target]}.`);
      }
    } catch (error) {
      toast.error(messageOf(error));
    } finally {
      setBusy(null);
      await queryClient.invalidateQueries({ queryKey: ['tickets'] });
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    }
  }

  function onDrop(event: DragEvent, target: TicketStatus) {
    event.preventDefault();
    setOverColumn(null);
    const id = event.dataTransfer.getData('text/plain') || dragging;
    setDragging(null);
    const ticket = tickets.data?.items.find((t) => t.id === id);
    if (ticket) void move(ticket, target);
  }

  return (
    <>
      <PageHeader
        title="Kanban"
        description="Arraste os cartões entre as colunas ou use o seletor “Mover para”."
        actions={<CheckboxField label="Somente os meus" checked={onlyMine} onChange={(e) => setOnlyMine(e.target.checked)} />}
      />

      {tickets.isPending ? (
        <SkeletonRows rows={5} />
      ) : tickets.isError ? (
        <ErrorState error={tickets.error} onRetry={() => void tickets.refetch()} />
      ) : (
        <>
          {tickets.data.totalCount > tickets.data.items.length && (
            <p role="status" className="mb-3 rounded-lg bg-warning-soft px-4 py-2.5 text-[13px] text-warning">
              Exibindo os {tickets.data.items.length} chamados de maior prioridade de {tickets.data.totalCount}. Use os filtros da lista para refinar.
            </p>
          )}
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {COLUMNS.map((column) => (
              <section
                key={column}
                aria-label={`${STATUS_LABEL[column]}: ${columns[column].length} chamados`}
                onDragOver={(e) => { e.preventDefault(); setOverColumn(column); }}
                onDragLeave={() => setOverColumn((c) => (c === column ? null : c))}
                onDrop={(e) => onDrop(e, column)}
                className={cn('flex min-h-48 flex-col rounded-xl border bg-surface-2/50 transition-colors', overColumn === column && dragging ? 'border-accent bg-accent-soft/40' : 'border-border')}
              >
                <h2 className="flex items-center justify-between px-4 py-3 text-[13px] font-semibold">
                  {STATUS_LABEL[column]}
                  <span className="rounded-full bg-neutral-soft px-2 py-0.5 text-xs text-muted">{columns[column].length}</span>
                </h2>
                <ul className="flex flex-1 flex-col gap-2.5 px-3 pb-3">
                  {columns[column].length === 0 && <li><EmptyState title="Vazio" /></li>}
                  {columns[column].map((t) => (
                    <li
                      key={t.id}
                      draggable
                      onDragStart={(e) => { e.dataTransfer.setData('text/plain', t.id); e.dataTransfer.effectAllowed = 'move'; setDragging(t.id); }}
                      onDragEnd={() => { setDragging(null); setOverColumn(null); }}
                      className={cn('cursor-grab rounded-lg border border-border bg-surface p-3 shadow-sm', dragging === t.id && 'opacity-50', busy === t.id && 'animate-pulse')}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <Link to={`/tickets/${t.id}`} className="min-w-0 text-[13px] font-semibold hover:text-accent">
                          <span className="block text-xs font-medium text-muted">{t.ticketNumber}</span>
                          <span className="line-clamp-2">{t.title}</span>
                        </Link>
                      </div>
                      <div className="mt-2 flex flex-wrap items-center gap-1.5">
                        <PriorityBadge name={t.priorityName} level={t.priorityLevel} />
                        <SlaBadge state={t.slaState} />
                      </div>
                      <p className="mt-2 text-xs text-muted">{t.assignedToName ?? 'Sem responsável'} • {t.requesterName}</p>

                      <div className="mt-2.5 flex items-center gap-2">
                        {t.status === 'NEW' && user.role === 'AGENT' && (
                          <Button size="sm" onClick={() => void move(t, 'IN_PROGRESS')} loading={busy === t.id}>
                            <HandHelping className="size-3.5" aria-hidden /> Assumir
                          </Button>
                        )}
                        <label className="ml-auto">
                          <span className="sr-only">Mover {t.ticketNumber} para</span>
                          <select
                            aria-label={`Mover ${t.ticketNumber} para`}
                            value=""
                            onChange={(e) => { if (e.target.value) void move(t, e.target.value as TicketStatus); }}
                            className="h-8 rounded-lg border border-border bg-surface px-2 text-xs"
                          >
                            <option value="">Mover para…</option>
                            {COLUMNS.filter((c) => c !== t.status && c !== 'NEW').map((c) => <option key={c} value={c}>{STATUS_LABEL[c]}</option>)}
                          </select>
                        </label>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            ))}
          </div>
        </>
      )}
    </>
  );
}
