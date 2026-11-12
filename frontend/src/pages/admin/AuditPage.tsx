import { useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common';
import { Card } from '@/components/ui/card';
import { SelectField, TextField } from '@/components/ui/fields';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { Pagination, Table, TableWrap, Td, Th, Tr } from '@/components/ui/table';
import { useAudit } from '@/features/insights/api';
import { useDebounce } from '@/hooks/useDebounce';
import { formatDateTime, HISTORY_LABEL, historyDescription } from '@/lib/format';
import type { HistoryAction } from '@/types/api';

export default function AuditPage() {
  const [ticketNumber, setTicketNumber] = useState('');
  const [action, setAction] = useState('');
  const [page, setPage] = useState(1);
  const debounced = useDebounce(ticketNumber.trim(), 300);

  const audit = useAudit({ ticketNumber: debounced || undefined, action: action || undefined, page, pageSize: 20 });

  return (
    <>
      <PageHeader title="Auditoria" description="Registro cronológico de tudo que aconteceu nos chamados, inclusive ações automáticas do sistema." />
      <Card>
        <form role="search" aria-label="Filtros de auditoria" onSubmit={(e) => e.preventDefault()} className="grid gap-3 border-b border-border p-4 sm:grid-cols-2 lg:grid-cols-3">
          <TextField label="Número do chamado" placeholder="HDP-2026-…" value={ticketNumber} onChange={(e) => { setTicketNumber(e.target.value); setPage(1); }} />
          <SelectField label="Ação" value={action} onChange={(e) => { setAction(e.target.value); setPage(1); }}>
            <option value="">Todas</option>
            {(Object.keys(HISTORY_LABEL) as HistoryAction[]).map((a) => <option key={a} value={a}>{HISTORY_LABEL[a]}</option>)}
          </SelectField>
        </form>

        <div aria-live="polite" aria-busy={audit.isFetching}>
          {audit.isPending ? <SkeletonRows rows={8} /> : audit.isError ? <ErrorState error={audit.error} onRetry={() => void audit.refetch()} /> : audit.data.items.length === 0 ? (
            <EmptyState title="Nenhum registro" description="Ajuste os filtros para ver outros eventos." />
          ) : (
            <>
              <TableWrap>
                <Table>
                  <caption className="sr-only">Registros de auditoria</caption>
                  <thead><tr><Th>Quando</Th><Th>Chamado</Th><Th>Evento</Th><Th>Responsável</Th></tr></thead>
                  <tbody>
                    {audit.data.items.map((e) => (
                      <Tr key={e.id}>
                        <Td className="whitespace-nowrap text-muted">{formatDateTime(e.createdAt)}</Td>
                        <Td><Link to={`/tickets/${e.ticketId}`} className="font-medium hover:text-accent">{e.ticketNumber}</Link></Td>
                        <Td>{historyDescription(e)}</Td>
                        <Td className="whitespace-nowrap">{e.actorId ? e.actorName : <span className="text-muted">{e.actorName}</span>}</Td>
                      </Tr>
                    ))}
                  </tbody>
                </Table>
              </TableWrap>
              <Pagination page={audit.data.page} totalPages={audit.data.totalPages} totalCount={audit.data.totalCount} pageSize={audit.data.pageSize} onPageChange={setPage} />
            </>
          )}
        </div>
      </Card>
    </>
  );
}
