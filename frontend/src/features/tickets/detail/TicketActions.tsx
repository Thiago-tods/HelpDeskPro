import { useState } from 'react';
import { HandHelping, Pencil } from 'lucide-react';
import { useCurrentUser } from '@/app/auth';
import { Button, type ButtonVariant } from '@/components/ui/button';
import { Card, CardBody, CardHeader } from '@/components/ui/card';
import { SelectField } from '@/components/ui/fields';
import { ConfirmDialog } from '@/components/ui/modal';
import { useToast } from '@/components/ui/toast';
import { useTeamMembers } from '@/features/catalog/api';
import { STATUS_ACTION_LABEL } from '@/lib/format';
import type { TicketDetail, TicketStatus } from '@/types/api';
import { useAssign, useChangeStatus } from '../api';

function actionLabel(ticket: TicketDetail, target: TicketStatus, isRequester: boolean): string {
  if (ticket.status === 'RESOLVED' && target === 'IN_PROGRESS') return 'Reabrir chamado';
  if (ticket.status === 'RESOLVED' && target === 'CLOSED' && isRequester) return 'Confirmar solução e fechar';
  if (ticket.status === 'WAITING_FOR_USER' && target === 'IN_PROGRESS' && isRequester) return 'Retomar atendimento';
  return STATUS_ACTION_LABEL[target];
}

const variantFor = (target: TicketStatus): ButtonVariant =>
  target === 'RESOLVED' || target === 'CLOSED' ? 'primary' : target === 'CANCELLED' ? 'danger' : 'secondary';

const CONFIRM_COPY: Partial<Record<TicketStatus, { title: string; description: string }>> = {
  CANCELLED: { title: 'Cancelar este chamado?', description: 'O chamado será encerrado como cancelado e não poderá mais ser alterado.' },
  CLOSED: { title: 'Fechar este chamado?', description: 'Depois de fechado, o chamado não poderá ser reaberto.' },
};

export function TicketActions({ ticket, onEdit }: { ticket: TicketDetail; onEdit: () => void }) {
  const user = useCurrentUser();
  const toast = useToast();
  const changeStatus = useChangeStatus(ticket.id);
  const assign = useAssign(ticket.id);
  const members = useTeamMembers(user.role === 'ADMIN' && ticket.teamId ? ticket.teamId : null);
  const [pending, setPending] = useState<TicketStatus | null>(null);
  const [assignee, setAssignee] = useState(ticket.assignedToId ?? '');

  const isFinal = ticket.status === 'CLOSED' || ticket.status === 'CANCELLED';
  const isRequester = user.role === 'REQUESTER';
  const showAssign = user.role === 'ADMIN' && !isFinal;
  const nothingToDo = ticket.allowedStatuses.length === 0 && !ticket.canAssignToMe && !ticket.canEdit && !showAssign;

  async function move(target: TicketStatus) {
    await changeStatus.mutateAsync(target);
    toast.success('Status atualizado.');
  }

  function onStatusClick(target: TicketStatus) {
    if (CONFIRM_COPY[target]) setPending(target);
    else void move(target).catch(() => undefined);
  }

  async function assignTo(userId: string | null, success: string) {
    try {
      await assign.mutateAsync(userId);
      toast.success(success);
    } catch {
      // toast global exibido
    }
  }

  return (
    <Card>
      <CardHeader title="Ações" as="h2" />
      <CardBody className="flex flex-col gap-3">
        {nothingToDo && <p className="text-[13px] text-muted">{isFinal ? 'Este chamado está finalizado.' : 'Nenhuma ação disponível para você neste momento.'}</p>}

        {ticket.canAssignToMe && (
          <Button loading={assign.isPending} onClick={() => void assignTo(user.id, 'Chamado assumido por você.')}>
            <HandHelping className="size-4" aria-hidden /> Assumir chamado
          </Button>
        )}

        {ticket.allowedStatuses.map((target) => (
          <Button key={target} variant={variantFor(target)} loading={changeStatus.isPending && changeStatus.variables === target} disabled={changeStatus.isPending} onClick={() => onStatusClick(target)}>
            {actionLabel(ticket, target, isRequester)}
          </Button>
        ))}

        {ticket.canEdit && (
          <Button variant="secondary" onClick={onEdit}>
            <Pencil className="size-4" aria-hidden /> Editar chamado
          </Button>
        )}

        {showAssign && (
          <div className="mt-1 flex flex-col gap-2 border-t border-border pt-3">
            <SelectField label="Responsável" value={assignee} onChange={(e) => setAssignee(e.target.value)} hint={ticket.teamName ? `Agentes da equipe ${ticket.teamName}.` : undefined}>
              <option value="">Sem responsável</option>
              {members.data?.map((m) => <option key={m.userId} value={m.userId}>{m.fullName}</option>)}
              {ticket.assignedToId && !members.data?.some((m) => m.userId === ticket.assignedToId) && (
                <option value={ticket.assignedToId}>{ticket.assignedToName}</option>
              )}
            </SelectField>
            <Button
              variant="secondary"
              loading={assign.isPending}
              disabled={assignee === (ticket.assignedToId ?? '')}
              onClick={() => void assignTo(assignee || null, assignee ? 'Responsável atualizado.' : 'Responsável removido.')}
            >
              Aplicar responsável
            </Button>
          </div>
        )}

        {user.role === 'AGENT' && ticket.assignedToId === user.id && !isFinal && (
          <Button variant="ghost" loading={assign.isPending} onClick={() => void assignTo(null, 'Chamado devolvido à fila.')}>
            Devolver à fila
          </Button>
        )}
      </CardBody>

      {pending && (
        <ConfirmDialog
          open
          onOpenChange={(open) => !open && setPending(null)}
          title={CONFIRM_COPY[pending]!.title}
          description={CONFIRM_COPY[pending]!.description}
          confirmLabel={pending === 'CANCELLED' ? 'Cancelar chamado' : 'Fechar chamado'}
          tone={pending === 'CANCELLED' ? 'danger' : 'primary'}
          onConfirm={() => move(pending)}
        />
      )}
    </Card>
  );
}
