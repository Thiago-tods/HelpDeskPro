import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useCurrentUser } from '@/app/auth';
import { Button } from '@/components/ui/button';
import { SelectField, TextAreaField, TextField } from '@/components/ui/fields';
import { Modal, ModalFooter } from '@/components/ui/modal';
import { useToast } from '@/components/ui/toast';
import { useCategories, usePriorities, useTeams } from '@/features/catalog/api';
import type { TicketDetail } from '@/types/api';
import { useUpdateTicket, type UpdateTicketInput } from '../api';

const schema = z.object({
  title: z.string().trim().min(5, 'Use ao menos 5 caracteres.').max(200, 'Use no máximo 200 caracteres.'),
  description: z.string().trim().min(10, 'Use ao menos 10 caracteres.').max(10000, 'Use no máximo 10.000 caracteres.'),
  categoryId: z.string().min(1, 'Escolha uma categoria.'),
  priorityId: z.string(),
  teamId: z.string(),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  ticket: TicketDetail;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function EditTicketModal({ ticket, open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const toast = useToast();
  const update = useUpdateTicket(ticket.id);
  const categories = useCategories();
  const priorities = usePriorities();
  const teams = useTeams(user.role === 'ADMIN');

  // Prioridade: equipe de atendimento. Equipe do chamado: apenas administradores. O servidor reforça as duas regras.
  const canPriority = user.role !== 'REQUESTER';
  const canTeam = user.role === 'ADMIN';

  const defaults: FormValues = {
    title: ticket.title,
    description: ticket.description,
    categoryId: ticket.categoryId,
    priorityId: ticket.priorityId,
    teamId: ticket.teamId ?? '',
  };

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: defaults });

  async function onSubmit(values: FormValues) {
    const changes: UpdateTicketInput = {};
    if (values.title !== defaults.title) changes.title = values.title;
    if (values.description !== defaults.description) changes.description = values.description;
    if (values.categoryId !== defaults.categoryId) changes.categoryId = values.categoryId;
    if (canPriority && values.priorityId && values.priorityId !== defaults.priorityId) changes.priorityId = values.priorityId;
    if (canTeam && values.teamId && values.teamId !== defaults.teamId) changes.teamId = values.teamId;

    if (Object.keys(changes).length === 0) {
      onOpenChange(false);
      return;
    }
    try {
      await update.mutateAsync(changes);
      toast.success('Chamado atualizado.');
      onOpenChange(false);
    } catch {
      // toast global exibido
    }
  }

  return (
    <Modal open={open} onOpenChange={onOpenChange} title="Editar chamado" description={ticket.ticketNumber}>
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
        <TextField label="Assunto" required error={errors.title?.message} {...register('title')} />
        <TextAreaField label="Descrição" required rows={6} error={errors.description?.message} {...register('description')} />
        <div className="grid gap-4 sm:grid-cols-2">
          <SelectField label="Categoria" error={errors.categoryId?.message} {...register('categoryId')}>
            {categories.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </SelectField>
          {canPriority && (
            <SelectField label="Prioridade" hint="Recalcula os prazos de SLA." {...register('priorityId')}>
              {priorities.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </SelectField>
          )}
          {canTeam && (
            <SelectField label="Equipe" {...register('teamId')}>
              {ticket.teamId === null && <option value="">Sem equipe</option>}
              {teams.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </SelectField>
          )}
        </div>
        <ModalFooter>
          <Button variant="secondary" onClick={() => onOpenChange(false)}>Cancelar</Button>
          <Button type="submit" loading={isSubmitting}>Salvar alterações</Button>
        </ModalFooter>
      </form>
    </Modal>
  );
}
