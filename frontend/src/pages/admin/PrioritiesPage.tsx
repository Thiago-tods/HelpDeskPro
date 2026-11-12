import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
import { z } from 'zod';
import { PageHeader, PriorityBadge } from '@/components/common';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { TextField } from '@/components/ui/fields';
import { ConfirmDialog, Modal, ModalFooter } from '@/components/ui/modal';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { Table, TableWrap, Td, Th, Tr } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { usePriorities, useSavePriority, useTogglePriority } from '@/features/catalog/api';
import { formatMinutes } from '@/lib/format';
import type { Priority } from '@/types/api';

const schema = z
  .object({
    name: z.string().trim().min(2, 'Informe o nome.').max(50),
    level: z.coerce.number().int('Use um número inteiro.').min(1, 'Mínimo 1.').max(10, 'Máximo 10.'),
    firstResponseMinutes: z.coerce.number().int('Use um número inteiro.').min(1, 'Mínimo 1 minuto.'),
    resolutionMinutes: z.coerce.number().int('Use um número inteiro.').min(1, 'Mínimo 1 minuto.'),
  })
  .refine((v) => v.resolutionMinutes >= v.firstResponseMinutes, { path: ['resolutionMinutes'], message: 'A resolução não pode ser menor que a primeira resposta.' });

type FormInput = z.input<typeof schema>;
type FormOutput = z.output<typeof schema>;

function PriorityModal({ priority, onClose }: { priority: Priority | null; onClose: () => void }) {
  const save = useSavePriority(priority?.id ?? null);
  const toast = useToast();
  const { register, handleSubmit, watch, formState: { errors, isSubmitting } } = useForm<FormInput, unknown, FormOutput>({
    resolver: zodResolver(schema),
    defaultValues: { name: priority?.name ?? '', level: priority?.level ?? 1, firstResponseMinutes: priority?.firstResponseMinutes ?? 240, resolutionMinutes: priority?.resolutionMinutes ?? 2880 },
  });

  const preview = (value: unknown) => formatMinutes(Number(value) || 0);

  async function onSubmit(values: FormOutput) {
    try {
      await save.mutateAsync(values);
      toast.success(priority ? 'Prioridade atualizada.' : 'Prioridade criada.');
      onClose();
    } catch {
      // toast global exibido
    }
  }

  return (
    <Modal open onOpenChange={(o) => !o && onClose()} title={priority ? 'Editar prioridade' : 'Nova prioridade'} description="Prazos em minutos corridos. Alterações valem para novos chamados.">
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
        <div className="grid gap-4 sm:grid-cols-[1fr_8rem]">
          <TextField label="Nome" required error={errors.name?.message} {...register('name')} />
          <TextField label="Nível" type="number" min={1} max={10} required hint="Maior = mais urgente." error={errors.level?.message} {...register('level')} />
        </div>
        <TextField label="Primeira resposta (minutos)" type="number" min={1} required hint={`= ${preview(watch('firstResponseMinutes'))}`} error={errors.firstResponseMinutes?.message} {...register('firstResponseMinutes')} />
        <TextField label="Resolução (minutos)" type="number" min={1} required hint={`= ${preview(watch('resolutionMinutes'))}`} error={errors.resolutionMinutes?.message} {...register('resolutionMinutes')} />
        <ModalFooter>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" loading={isSubmitting}>Salvar</Button>
        </ModalFooter>
      </form>
    </Modal>
  );
}

export default function PrioritiesPage() {
  const toast = useToast();
  const priorities = usePriorities();
  const toggle = useTogglePriority();
  const [form, setForm] = useState<Priority | 'new' | null>(null);
  const [toggling, setToggling] = useState<Priority | null>(null);

  return (
    <>
      <PageHeader title="Prioridades" description="Níveis de urgência e seus prazos de atendimento." actions={<Button onClick={() => setForm('new')}><Plus className="size-4" aria-hidden /> Nova prioridade</Button>} />
      <Card>
        {priorities.isPending ? <SkeletonRows rows={4} /> : priorities.isError ? <ErrorState error={priorities.error} onRetry={() => void priorities.refetch()} /> : priorities.data.length === 0 ? (
          <EmptyState title="Nenhuma prioridade" />
        ) : (
          <TableWrap>
            <Table>
              <caption className="sr-only">Prioridades</caption>
              <thead><tr><Th>Prioridade</Th><Th>Nível</Th><Th>1ª resposta</Th><Th>Resolução</Th><Th>Situação</Th><Th className="text-right">Ações</Th></tr></thead>
              <tbody>
                {priorities.data.map((p) => (
                  <Tr key={p.id}>
                    <Td><PriorityBadge name={p.name} level={p.level} /></Td>
                    <Td>{p.level}</Td>
                    <Td>{formatMinutes(p.firstResponseMinutes)}</Td>
                    <Td>{formatMinutes(p.resolutionMinutes)}</Td>
                    <Td><Badge tone={p.isActive ? 'success' : 'neutral'}>{p.isActive ? 'Ativa' : 'Desativada'}</Badge></Td>
                    <Td className="text-right whitespace-nowrap">
                      <Button variant="ghost" size="sm" onClick={() => setForm(p)} aria-label={`Editar ${p.name}`}>Editar</Button>
                      <Button variant="ghost" size="sm" onClick={() => setToggling(p)} aria-label={`${p.isActive ? 'Desativar' : 'Reativar'} ${p.name}`}>{p.isActive ? 'Desativar' : 'Reativar'}</Button>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          </TableWrap>
        )}
      </Card>

      {form && <PriorityModal priority={form === 'new' ? null : form} onClose={() => setForm(null)} />}
      {toggling && (
        <ConfirmDialog
          open
          onOpenChange={(o) => !o && setToggling(null)}
          title={toggling.isActive ? `Desativar “${toggling.name}”?` : `Reativar “${toggling.name}”?`}
          description={toggling.isActive ? 'A prioridade deixa de estar disponível para novos chamados.' : 'A prioridade volta a ficar disponível.'}
          confirmLabel={toggling.isActive ? 'Desativar' : 'Reativar'}
          tone={toggling.isActive ? 'danger' : 'primary'}
          onConfirm={async () => {
            await toggle.mutateAsync({ id: toggling.id, isActive: !toggling.isActive });
            toast.success(toggling.isActive ? 'Prioridade desativada.' : 'Prioridade reativada.');
          }}
        />
      )}
    </>
  );
}
