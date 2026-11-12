import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Paperclip, X } from 'lucide-react';
import { z } from 'zod';
import { PageHeader } from '@/components/common';
import { Button, IconButton } from '@/components/ui/button';
import { Card, CardBody } from '@/components/ui/card';
import { SelectField, TextAreaField, TextField } from '@/components/ui/fields';
import { PageLoader } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { useCategories, usePriorities } from '@/features/catalog/api';
import { uploadAttachment, useCreateTicket } from '@/features/tickets/api';
import { formatBytes, formatMinutes } from '@/lib/format';
import { messageOf } from '@/lib/api';

const MAX_FILE_BYTES = 10 * 1024 * 1024; // o limite definitivo é o do servidor
const MAX_FILES = 5;
const ACCEPT = '.png,.jpg,.jpeg,.gif,.pdf,.txt,.csv,.docx,.xlsx';

const schema = z.object({
  title: z.string().trim().min(5, 'Descreva o assunto em ao menos 5 caracteres.').max(200, 'Use no máximo 200 caracteres.'),
  description: z.string().trim().min(10, 'Conte mais detalhes (mínimo de 10 caracteres).').max(10000, 'Use no máximo 10.000 caracteres.'),
  categoryId: z.string().min(1, 'Escolha uma categoria.'),
  priorityId: z.string().min(1, 'Escolha uma prioridade.'),
});

type FormValues = z.infer<typeof schema>;

export default function NewTicketPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const categories = useCategories();
  const priorities = usePriorities();
  const create = useCreateTicket();
  const [files, setFiles] = useState<File[]>([]);

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { title: '', description: '', categoryId: '', priorityId: '' } });

  const selectedPriority = priorities.data?.find((p) => p.id === watch('priorityId'));

  function addFiles(list: FileList | null) {
    if (!list) return;
    const next = [...files];
    for (const file of Array.from(list)) {
      if (file.size > MAX_FILE_BYTES) toast.error(`"${file.name}" excede ${formatBytes(MAX_FILE_BYTES)}.`);
      else if (next.length >= MAX_FILES) toast.error(`Anexe no máximo ${MAX_FILES} arquivos ao abrir o chamado.`);
      else next.push(file);
    }
    setFiles(next);
  }

  async function onSubmit(values: FormValues) {
    let ticketId: string;
    try {
      ticketId = (await create.mutateAsync(values)).id;
    } catch {
      return; // o toast global já informou o motivo
    }

    const failed: string[] = [];
    for (const file of files) {
      try {
        await uploadAttachment(ticketId, file);
      } catch (error) {
        failed.push(`${file.name}: ${messageOf(error)}`);
      }
    }
    if (failed.length) toast.error(`Chamado aberto, mas alguns anexos falharam — ${failed.join(' | ')}`);
    else toast.success('Chamado aberto com sucesso.');
    navigate(`/tickets/${ticketId}`);
  }

  if (categories.isPending || priorities.isPending) return <PageLoader />;

  return (
    <>
      <PageHeader title="Abrir chamado" description="Descreva o problema com o máximo de detalhes para agilizar o atendimento." />
      <Card className="max-w-3xl">
        <CardBody>
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-5">
            <TextField label="Assunto" required placeholder="Ex.: Não consigo acessar o sistema financeiro" error={errors.title?.message} {...register('title')} />

            <div className="grid gap-5 sm:grid-cols-2">
              <SelectField label="Categoria" required error={errors.categoryId?.message} {...register('categoryId')}>
                <option value="">Selecione…</option>
                {categories.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </SelectField>
              <SelectField
                label="Prioridade"
                required
                error={errors.priorityId?.message}
                hint={selectedPriority ? `Meta: 1ª resposta em ${formatMinutes(selectedPriority.firstResponseMinutes)} • resolução em ${formatMinutes(selectedPriority.resolutionMinutes)}.` : 'Escolha conforme o impacto no seu trabalho.'}
                {...register('priorityId')}
              >
                <option value="">Selecione…</option>
                {priorities.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </SelectField>
            </div>

            <TextAreaField label="Descrição" required rows={7} placeholder="O que aconteceu? Desde quando? Já tentou algo?" error={errors.description?.message} {...register('description')} />

            <div>
              <p className="text-[13px] font-medium">Anexos <span className="font-normal text-muted">(opcional)</span></p>
              <label className="mt-1.5 flex cursor-pointer items-center gap-2 rounded-lg border border-dashed border-border px-4 py-3 text-[13px] text-muted hover:border-accent hover:text-text focus-within:outline-2 focus-within:outline-accent">
                <Paperclip className="size-4" aria-hidden />
                <span>Selecionar arquivos (png, jpg, gif, pdf, txt, csv, docx, xlsx — até 10 MB)</span>
                <input type="file" multiple accept={ACCEPT} className="sr-only" onChange={(e) => { addFiles(e.target.files); e.target.value = ''; }} />
              </label>
              {files.length > 0 && (
                <ul className="mt-2 flex flex-col gap-1.5">
                  {files.map((f, i) => (
                    <li key={`${f.name}-${i}`} className="flex items-center justify-between rounded-lg bg-surface-2 px-3 py-1.5 text-[13px]">
                      <span className="truncate">{f.name} <span className="text-muted">({formatBytes(f.size)})</span></span>
                      <IconButton label={`Remover ${f.name}`} className="size-7" onClick={() => setFiles(files.filter((_, j) => j !== i))}>
                        <X className="size-4" aria-hidden />
                      </IconButton>
                    </li>
                  ))}
                </ul>
              )}
            </div>

            <div className="flex justify-end gap-2 border-t border-border pt-4">
              <Link to="/tickets"><Button variant="secondary">Cancelar</Button></Link>
              <Button type="submit" loading={isSubmitting}>Abrir chamado</Button>
            </div>
          </form>
        </CardBody>
      </Card>
    </>
  );
}
