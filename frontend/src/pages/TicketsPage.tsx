import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { useCurrentUser } from '@/app/auth';
import { PageHeader } from '@/components/common';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CheckboxField, SelectField, TextField } from '@/components/ui/fields';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { Pagination } from '@/components/ui/table';
import { useAgents, useCategories, usePriorities, useTeams } from '@/features/catalog/api';
import { TicketTable } from '@/features/tickets/TicketTable';
import { useTickets, type TicketFilters } from '@/features/tickets/api';
import { useDebounce } from '@/hooks/useDebounce';
import { ACTIVE_STATUSES, STATUS_LABEL } from '@/lib/format';
import type { TicketStatus } from '@/types/api';

type Preset = 'default' | 'queue' | 'assigned';

const PAGE_SIZE = 15;
const ACTIVE = ACTIVE_STATUSES.join(',');

const SORT_OPTIONS = [
  { value: 'createdAt', label: 'Data de abertura' },
  { value: 'updatedAt', label: 'Última atualização' },
  { value: 'priority', label: 'Prioridade' },
  { value: 'status', label: 'Status' },
  { value: 'ticketNumber', label: 'Número' },
  { value: 'title', label: 'Título' },
];

function TicketsView({ preset }: { preset: Preset }) {
  const user = useCurrentUser();
  const isStaff = user.role !== 'REQUESTER';
  const [params, setParams] = useSearchParams();

  const defaultStatus = preset === 'default' ? '' : ACTIVE;
  const status = params.get('status') ?? defaultStatus;
  const priorityId = params.get('priority') ?? '';
  const categoryId = params.get('category') ?? '';
  const teamId = params.get('team') ?? '';
  const agentId = params.get('agent') ?? '';
  const overdue = params.get('overdue') === '1';
  const sortBy = params.get('sort') ?? 'createdAt';
  const sortDir = (params.get('dir') ?? 'desc') as 'asc' | 'desc';
  const page = Math.max(1, Number(params.get('page') ?? 1) || 1);

  // Busca com debounce: o campo é local e só atualiza a URL (e a consulta) após uma pausa na digitação.
  const [search, setSearch] = useState(params.get('q') ?? '');
  const debouncedSearch = useDebounce(search.trim(), 300);

  function update(changes: Record<string, string | null>, resetPage = true) {
    setParams((current) => {
      const next = new URLSearchParams(current);
      for (const [key, value] of Object.entries(changes)) {
        if (value === null || value === '') next.delete(key);
        else next.set(key, value);
      }
      if (resetPage) next.delete('page');
      return next;
    }, { replace: true });
  }

  useEffect(() => {
    if ((params.get('q') ?? '') !== debouncedSearch) update({ q: debouncedSearch });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch]);

  const filters: TicketFilters = useMemo(
    () => ({
      search: params.get('q') ?? undefined,
      status: status || undefined,
      priorityId: priorityId || undefined,
      categoryId: categoryId || undefined,
      teamId: teamId || undefined,
      assignedToId: agentId || undefined,
      unassigned: preset === 'queue' || undefined,
      mine: preset === 'assigned' || undefined,
      overdue: overdue || undefined,
      sortBy,
      sortDir,
      page,
      pageSize: PAGE_SIZE,
    }),
    [params, status, priorityId, categoryId, teamId, agentId, overdue, preset, sortBy, sortDir, page],
  );

  const tickets = useTickets(filters);
  const categories = useCategories();
  const priorities = usePriorities();
  const teams = useTeams(isStaff);
  const agents = useAgents(isStaff && preset === 'default');

  const titles: Record<Preset, { title: string; description: string }> = {
    default: isStaff
      ? { title: 'Todos os chamados', description: 'Chamados do seu escopo de atendimento.' }
      : { title: 'Meus chamados', description: 'Acompanhe o andamento das suas solicitações.' },
    queue: { title: 'Fila de atendimento', description: 'Chamados sem responsável aguardando alguém assumir.' },
    assigned: { title: 'Atribuídos a mim', description: 'Chamados sob sua responsabilidade.' },
  };

  const hasFilters = Boolean(params.get('q') || params.get('status') || priorityId || categoryId || teamId || agentId || overdue);
  const data = tickets.data;

  return (
    <>
      <PageHeader
        title={titles[preset].title}
        description={titles[preset].description}
        actions={
          <Link to="/tickets/new">
            <Button><Plus className="size-4" aria-hidden /> Abrir chamado</Button>
          </Link>
        }
      />

      <Card>
        <form role="search" aria-label="Filtros de chamados" onSubmit={(e) => e.preventDefault()} className="grid gap-3 border-b border-border p-4 sm:grid-cols-2 lg:grid-cols-4">
          <TextField
            wrapperClassName="sm:col-span-2"
            label="Buscar"
            type="search"
            placeholder="Número, título ou descrição"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />

          <SelectField label="Status" value={status} onChange={(e) => update({ status: e.target.value })}>
            {preset === 'default' && <option value="">Todos</option>}
            <option value={ACTIVE}>Ativos</option>
            {(Object.keys(STATUS_LABEL) as TicketStatus[]).map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
          </SelectField>

          <SelectField label="Prioridade" value={priorityId} onChange={(e) => update({ priority: e.target.value })}>
            <option value="">Todas</option>
            {priorities.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </SelectField>

          <SelectField label="Categoria" value={categoryId} onChange={(e) => update({ category: e.target.value })}>
            <option value="">Todas</option>
            {categories.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </SelectField>

          {isStaff && (
            <SelectField label="Equipe" value={teamId} onChange={(e) => update({ team: e.target.value })}>
              <option value="">Todas</option>
              {teams.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </SelectField>
          )}

          {isStaff && preset === 'default' && (
            <SelectField label="Responsável" value={agentId} onChange={(e) => update({ agent: e.target.value })}>
              <option value="">Qualquer</option>
              {agents.data?.map((a) => <option key={a.id} value={a.id}>{a.fullName}</option>)}
            </SelectField>
          )}

          <SelectField label="Ordenar por" value={sortBy} onChange={(e) => update({ sort: e.target.value })}>
            {SORT_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </SelectField>

          <SelectField label="Ordem" value={sortDir} onChange={(e) => update({ dir: e.target.value })}>
            <option value="desc">Mais recentes / maiores primeiro</option>
            <option value="asc">Mais antigos / menores primeiro</option>
          </SelectField>

          {isStaff && (
            <div className="flex items-end pb-2.5">
              <CheckboxField label="Somente atrasados" checked={overdue} onChange={(e) => update({ overdue: e.target.checked ? '1' : null })} />
            </div>
          )}

          {hasFilters && (
            <div className="flex items-end">
              <Button variant="ghost" onClick={() => { setSearch(''); setParams({}, { replace: true }); }}>Limpar filtros</Button>
            </div>
          )}
        </form>

        <div aria-live="polite" aria-busy={tickets.isFetching}>
          {tickets.isPending ? (
            <SkeletonRows rows={6} />
          ) : tickets.isError ? (
            <ErrorState error={tickets.error} onRetry={() => void tickets.refetch()} />
          ) : data && data.items.length === 0 ? (
            <EmptyState
              title={hasFilters ? 'Nenhum chamado encontrado' : 'Nenhum chamado por aqui'}
              description={hasFilters ? 'Ajuste ou limpe os filtros para ver mais resultados.' : preset === 'default' && !isStaff ? 'Quando precisar de ajuda, abra um chamado.' : 'Nada pendente neste momento.'}
              action={!isStaff && !hasFilters ? <Link to="/tickets/new"><Button>Abrir chamado</Button></Link> : undefined}
            />
          ) : (
            data && (
              <>
                <TicketTable items={data.items} showPeople={isStaff} />
                <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} pageSize={data.pageSize} onPageChange={(p) => update({ page: String(p) }, false)} />
              </>
            )
          )}
        </div>
      </Card>
    </>
  );
}

export default function TicketsPage() {
  return <TicketsView preset="default" />;
}

export function QueuePage() {
  return <TicketsView preset="queue" />;
}

export function AssignedPage() {
  return <TicketsView preset="assigned" />;
}
