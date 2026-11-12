import { Link } from 'react-router-dom';
import { PriorityBadge, SlaBadge, StatusBadge } from '@/components/common';
import { Table, TableWrap, Td, Th, Tr } from '@/components/ui/table';
import { formatRelative } from '@/lib/format';
import type { TicketListItem } from '@/types/api';

interface TicketTableProps {
  items: TicketListItem[];
  /** Mostra solicitante e responsável (equipe de atendimento). */
  showPeople?: boolean;
  compact?: boolean;
}

export function TicketTable({ items, showPeople = false, compact = false }: TicketTableProps) {
  return (
    <TableWrap>
      <Table>
        <caption className="sr-only">Lista de chamados</caption>
        <thead>
          <tr>
            <Th>Chamado</Th>
            <Th>Status</Th>
            <Th className="hidden sm:table-cell">Prioridade</Th>
            <Th className="hidden md:table-cell">SLA</Th>
            {showPeople && !compact && <Th className="hidden lg:table-cell">Solicitante</Th>}
            {showPeople && <Th className="hidden lg:table-cell">Responsável</Th>}
            {!compact && <Th className="hidden xl:table-cell">Atualizado</Th>}
          </tr>
        </thead>
        <tbody>
          {items.map((t) => (
            <Tr key={t.id}>
              <Td className="max-w-[26rem] min-w-56">
                <Link to={`/tickets/${t.id}`} className="block rounded font-semibold text-text hover:text-accent">
                  <span className="mr-2 text-xs font-medium whitespace-nowrap text-muted">{t.ticketNumber}</span>
                  <span className="block truncate sm:inline">{t.title}</span>
                </Link>
                {!compact && <p className="mt-0.5 text-xs text-muted">{t.categoryName}{t.teamName ? ` • ${t.teamName}` : ''}</p>}
              </Td>
              <Td><StatusBadge status={t.status} /></Td>
              <Td className="hidden sm:table-cell"><PriorityBadge name={t.priorityName} level={t.priorityLevel} /></Td>
              <Td className="hidden md:table-cell"><SlaBadge state={t.slaState} /></Td>
              {showPeople && !compact && <Td className="hidden whitespace-nowrap lg:table-cell">{t.requesterName}</Td>}
              {showPeople && (
                <Td className="hidden whitespace-nowrap lg:table-cell">
                  {t.assignedToName ?? <span className="text-muted">Livre</span>}
                </Td>
              )}
              {!compact && <Td className="hidden whitespace-nowrap text-muted xl:table-cell">{formatRelative(t.updatedAt)}</Td>}
            </Tr>
          ))}
        </tbody>
      </Table>
    </TableWrap>
  );
}
