import { useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, Clock, Hourglass, Inbox, MessageSquareReply, Plus, Ticket, Timer, Wrench } from 'lucide-react';
import { useCurrentUser } from '@/app/auth';
import { PageHeader, StatCard } from '@/components/common';
import { BarsChart, TimelineChart } from '@/components/charts';
import { Button } from '@/components/ui/button';
import { Card, CardBody, CardHeader } from '@/components/ui/card';
import { SelectField } from '@/components/ui/fields';
import { EmptyState, ErrorState, Skeleton, SkeletonRows } from '@/components/ui/states';
import { useDashboard } from '@/features/insights/api';
import { TicketTable } from '@/features/tickets/TicketTable';
import { useTickets, type TicketFilters } from '@/features/tickets/api';
import { formatMinutes, formatRelative } from '@/lib/format';

function RecentTickets({ title, description, filters, to, showPeople, emptyText }: { title: string; description?: string; filters: TicketFilters; to: string; showPeople?: boolean; emptyText: string }) {
  const tickets = useTickets({ pageSize: 5, ...filters });
  return (
    <Card>
      <CardHeader title={title} description={description} actions={<Link to={to} className="text-[13px] font-medium text-accent hover:underline">Ver todos</Link>} />
      {tickets.isPending ? (
        <SkeletonRows rows={3} />
      ) : tickets.isError ? (
        <ErrorState error={tickets.error} onRetry={() => void tickets.refetch()} />
      ) : tickets.data.items.length === 0 ? (
        <EmptyState title={emptyText} />
      ) : (
        <TicketTable items={tickets.data.items} showPeople={showPeople} compact />
      )}
    </Card>
  );
}

function RequesterDashboard() {
  const user = useCurrentUser();
  const summary = useDashboard(30);
  const s = summary.data;

  return (
    <>
      <PageHeader
        title={`Olá, ${user.fullName.split(' ')[0]}`}
        description="Acompanhe suas solicitações de suporte."
        actions={<Link to="/tickets/new"><Button><Plus className="size-4" aria-hidden /> Abrir chamado</Button></Link>}
      />

      {summary.isError ? (
        <Card><ErrorState error={summary.error} onRetry={() => void summary.refetch()} /></Card>
      ) : (
        <section aria-label="Resumo dos seus chamados" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Total de chamados" value={s?.total ?? <Skeleton className="h-7 w-10" />} icon={<Ticket className="size-5" />} to="/tickets" />
          <StatCard label="Em aberto" value={s ? s.open + s.inProgress : <Skeleton className="h-7 w-10" />} icon={<Wrench className="size-5" />} tone="info" hint="Novos e em atendimento" to="/tickets?status=NEW,IN_PROGRESS" />
          <StatCard label="Aguardando sua resposta" value={s?.waitingForUser ?? <Skeleton className="h-7 w-10" />} icon={<MessageSquareReply className="size-5" />} tone="warning" to="/tickets?status=WAITING_FOR_USER" />
          <StatCard label="Resolvidos" value={s ? s.resolved + s.closed : <Skeleton className="h-7 w-10" />} icon={<CheckCircle2 className="size-5" />} tone="success" to="/tickets?status=RESOLVED,CLOSED" />
        </section>
      )}

      <div className="mt-6">
        <RecentTickets title="Atualizados recentemente" filters={{ sortBy: 'updatedAt', sortDir: 'desc' }} to="/tickets" emptyText="Você ainda não abriu chamados" />
      </div>
    </>
  );
}

function StaffDashboard() {
  const user = useCurrentUser();
  const [days, setDays] = useState(30);
  const summary = useDashboard(days);
  const s = summary.data;
  const isAdmin = user.role === 'ADMIN';

  const num = (value: number | undefined) => value ?? <Skeleton className="h-7 w-10" />;

  return (
    <>
      <PageHeader
        title={isAdmin ? 'Visão geral' : 'Painel de atendimento'}
        description={s ? `Indicadores atualizados às ${formatRelative(s.generatedAt)}.` : 'Carregando indicadores…'}
        actions={
          <SelectField label="Período" hideLabel value={String(days)} onChange={(e) => setDays(Number(e.target.value))} className="w-44">
            <option value="7">Últimos 7 dias</option>
            <option value="30">Últimos 30 dias</option>
            <option value="90">Últimos 90 dias</option>
          </SelectField>
        }
      />

      {summary.isError && !s ? (
        <Card><ErrorState error={summary.error} onRetry={() => void summary.refetch()} /></Card>
      ) : (
        <>
          <section aria-label="Indicadores atuais" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Total de chamados" value={num(s?.total)} icon={<Ticket className="size-5" />} to="/tickets" />
            <StatCard label="Abertos (novos)" value={num(s?.open)} icon={<Inbox className="size-5" />} tone="info" to="/queue" />
            <StatCard label="Em atendimento" value={num(s?.inProgress)} icon={<Wrench className="size-5" />} to="/tickets?status=IN_PROGRESS" />
            <StatCard label="Aguardando usuário" value={num(s?.waitingForUser)} icon={<Hourglass className="size-5" />} tone="warning" to="/tickets?status=WAITING_FOR_USER" />
            <StatCard label="Resolvidos" value={num(s ? s.resolved + s.closed : undefined)} icon={<CheckCircle2 className="size-5" />} tone="success" hint={s ? `${s.closed} fechados` : undefined} to="/tickets?status=RESOLVED,CLOSED" />
            <StatCard label="Atrasados" value={num(s?.overdue)} icon={<AlertTriangle className="size-5" />} tone="danger" hint="SLA vencido" to="/tickets?overdue=1" />
            <StatCard label="Tempo médio 1ª resposta" value={s ? formatMinutes(s.avgFirstResponseMinutes) : <Skeleton className="h-7 w-16" />} icon={<Clock className="size-5" />} tone="info" hint={`Chamados dos últimos ${days} dias`} />
            <StatCard label="Tempo médio de resolução" value={s ? formatMinutes(s.avgResolutionMinutes) : <Skeleton className="h-7 w-16" />} icon={<Timer className="size-5" />} tone="info" hint={`Chamados dos últimos ${days} dias`} />
          </section>

          <div className="mt-6 grid gap-6 xl:grid-cols-3">
            <Card className="xl:col-span-2">
              <CardHeader title="Evolução diária" description="Chamados criados e resolvidos por dia (horário de Brasília)." />
              <CardBody>{s ? <TimelineChart data={s.timeline} /> : <Skeleton className="h-64 w-full" />}</CardBody>
            </Card>
            <Card>
              <CardHeader title="Por prioridade" description={`Chamados abertos nos últimos ${days} dias.`} />
              <CardBody>{s ? <BarsChart title="Chamados por prioridade" data={s.byPriority} color="var(--chart-3)" /> : <Skeleton className="h-48 w-full" />}</CardBody>
            </Card>
            <Card className="xl:col-span-2">
              <CardHeader title="Por categoria" />
              <CardBody>{s ? <BarsChart title="Chamados por categoria" data={s.byCategory} color="var(--chart-1)" /> : <Skeleton className="h-48 w-full" />}</CardBody>
            </Card>
            <Card>
              <CardHeader title="Por agente" description="Atribuídos no período; ativos e atrasados agora." />
              <CardBody className="px-0 py-2">
                {!s ? <SkeletonRows rows={3} /> : s.byAgent.length === 0 ? (
                  <EmptyState title="Sem atribuições" />
                ) : (
                  <ul className="divide-y divide-border">
                    {s.byAgent.map((a) => (
                      <li key={a.id} className="flex items-center justify-between gap-3 px-5 py-2.5">
                        <span className="min-w-0 truncate text-[13px] font-medium">{a.name}</span>
                        <span className="flex shrink-0 gap-3 text-xs text-muted">
                          <span title="Total no período"><strong className="text-text">{a.total}</strong> total</span>
                          <span title="Ativos agora"><strong className="text-text">{a.active}</strong> ativos</span>
                          {a.overdue > 0 && <span className="text-danger" title="Atrasados"><strong>{a.overdue}</strong> atrasados</span>}
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </CardBody>
            </Card>
          </div>

          <div className="mt-6 grid gap-6 xl:grid-cols-2">
            <RecentTickets title="Atrasados" description="SLA vencido e ainda em aberto." filters={{ overdue: true, sortBy: 'createdAt', sortDir: 'asc' }} to="/tickets?overdue=1" showPeople emptyText="Nenhum chamado atrasado" />
            <RecentTickets title="Fila livre" description="Sem responsável." filters={{ unassigned: true, status: 'NEW,IN_PROGRESS,WAITING_FOR_USER', sortBy: 'priority', sortDir: 'desc' }} to="/queue" showPeople emptyText="A fila está vazia" />
          </div>
        </>
      )}
    </>
  );
}

export default function HomePage() {
  const user = useCurrentUser();
  return user.role === 'REQUESTER' ? <RequesterDashboard /> : <StaffDashboard />;
}
