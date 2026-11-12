import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Info } from 'lucide-react';
import { z } from 'zod';
import { PageHeader, PriorityBadge } from '@/components/common';
import { Button } from '@/components/ui/button';
import { Card, CardBody, CardHeader } from '@/components/ui/card';
import { TextField } from '@/components/ui/fields';
import { Modal, ModalFooter } from '@/components/ui/modal';
import { ErrorState, SkeletonRows } from '@/components/ui/states';
import { Table, TableWrap, Td, Th, Tr } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { usePriorities, useSavePriority } from '@/features/catalog/api';
import { useSettings, useSlaReport } from '@/features/insights/api';
import { formatMinutes, formatPercent } from '@/lib/format';
import type { Priority } from '@/types/api';

const part = z.coerce.number().int('Use números inteiros.').min(0, 'Não pode ser negativo.');
const schema = z
  .object({ frHours: part, frMinutes: part.max(59, 'Máximo 59.'), resHours: part, resMinutes: part.max(59, 'Máximo 59.') })
  .refine((v) => v.frHours * 60 + v.frMinutes > 0, { path: ['frMinutes'], message: 'Informe um prazo maior que zero.' })
  .refine((v) => v.resHours * 60 + v.resMinutes >= v.frHours * 60 + v.frMinutes, { path: ['resMinutes'], message: 'A resolução não pode ser menor que a primeira resposta.' });

type FormInput = z.input<typeof schema>;
type FormOutput = z.output<typeof schema>;

function DeadlinesModal({ priority, onClose }: { priority: Priority; onClose: () => void }) {
  const save = useSavePriority(priority.id);
  const toast = useToast();
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<FormInput, unknown, FormOutput>({
    resolver: zodResolver(schema),
    defaultValues: {
      frHours: Math.floor(priority.firstResponseMinutes / 60),
      frMinutes: priority.firstResponseMinutes % 60,
      resHours: Math.floor(priority.resolutionMinutes / 60),
      resMinutes: priority.resolutionMinutes % 60,
    },
  });

  async function onSubmit(v: FormOutput) {
    try {
      await save.mutateAsync({
        name: priority.name,
        level: priority.level,
        firstResponseMinutes: v.frHours * 60 + v.frMinutes,
        resolutionMinutes: v.resHours * 60 + v.resMinutes,
      });
      toast.success('Prazos atualizados. Valem para novos chamados.');
      onClose();
    } catch {
      // toast global exibido
    }
  }

  return (
    <Modal open onOpenChange={(o) => !o && onClose()} title={`Prazos — ${priority.name}`} description="Horas corridas, sem calendário de feriados.">
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-5">
        <fieldset className="grid grid-cols-2 gap-3">
          <legend className="mb-2 text-[13px] font-semibold">Primeira resposta</legend>
          <TextField label="Horas" type="number" min={0} error={errors.frHours?.message} {...register('frHours')} />
          <TextField label="Minutos" type="number" min={0} max={59} error={errors.frMinutes?.message} {...register('frMinutes')} />
        </fieldset>
        <fieldset className="grid grid-cols-2 gap-3">
          <legend className="mb-2 text-[13px] font-semibold">Resolução</legend>
          <TextField label="Horas" type="number" min={0} error={errors.resHours?.message} {...register('resHours')} />
          <TextField label="Minutos" type="number" min={0} max={59} error={errors.resMinutes?.message} {...register('resMinutes')} />
        </fieldset>
        <ModalFooter>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" loading={isSubmitting}>Salvar prazos</Button>
        </ModalFooter>
      </form>
    </Modal>
  );
}

export default function SlaPage() {
  const priorities = usePriorities();
  const settings = useSettings();
  const [from] = useState(() => new Date(Date.now() - 30 * 86_400_000).toISOString());
  const report = useSlaReport({ from });
  const [editing, setEditing] = useState<Priority | null>(null);

  const rateOf = (id: string) => report.data?.rows.find((r) => r.priorityId === id);

  return (
    <>
      <PageHeader title="SLA" description="Prazos de atendimento por prioridade e o desempenho dos últimos 30 dias." />

      <Card className="mb-6">
        <CardBody className="flex items-start gap-3 text-[13px]">
          <Info className="mt-0.5 size-5 shrink-0 text-info" aria-hidden />
          <p className="text-muted">
            O SLA conta horas corridas. Somente a resposta <strong className="text-text">pública</strong> de um agente conta como primeira resposta; notas internas não contam.
            {settings.data && <> O sistema avisa quando {settings.data.sla.warningPercent}% do prazo é consumido e confere os prazos a cada {settings.data.sla.checkIntervalSeconds}s.</>}
          </p>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Prazos por prioridade" description="Alterações valem apenas para novos chamados." />
        {priorities.isPending ? <SkeletonRows rows={4} /> : priorities.isError ? <ErrorState error={priorities.error} onRetry={() => void priorities.refetch()} /> : (
          <TableWrap>
            <Table>
              <caption className="sr-only">Prazos de SLA por prioridade</caption>
              <thead><tr><Th>Prioridade</Th><Th>1ª resposta</Th><Th>Resolução</Th><Th>Chamados (30d)</Th><Th>Conformidade</Th><Th className="text-right">Ações</Th></tr></thead>
              <tbody>
                {priorities.data.map((p) => {
                  const row = rateOf(p.id);
                  return (
                    <Tr key={p.id}>
                      <Td><PriorityBadge name={p.name} level={p.level} /></Td>
                      <Td>{formatMinutes(p.firstResponseMinutes)}</Td>
                      <Td>{formatMinutes(p.resolutionMinutes)}</Td>
                      <Td>{row?.total ?? 0}</Td>
                      <Td>{row ? formatPercent(row.complianceRate) : '—'}</Td>
                      <Td className="text-right"><Button variant="ghost" size="sm" onClick={() => setEditing(p)} aria-label={`Editar prazos de ${p.name}`}>Editar prazos</Button></Td>
                    </Tr>
                  );
                })}
              </tbody>
            </Table>
          </TableWrap>
        )}
      </Card>

      {editing && <DeadlinesModal priority={editing} onClose={() => setEditing(null)} />}
    </>
  );
}
