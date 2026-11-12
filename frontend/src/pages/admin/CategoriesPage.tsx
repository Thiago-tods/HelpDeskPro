import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
import { z } from 'zod';
import { PageHeader } from '@/components/common';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { SelectField, TextAreaField, TextField } from '@/components/ui/fields';
import { ConfirmDialog, Modal, ModalFooter } from '@/components/ui/modal';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { Table, TableWrap, Td, Th, Tr } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { useCategories, useSaveCategory, useTeams, useToggleCategory } from '@/features/catalog/api';
import type { Category } from '@/types/api';

const schema = z.object({ name: z.string().trim().min(2, 'Informe o nome.').max(100), description: z.string().trim().max(500), defaultTeamId: z.string() });

function CategoryModal({ category, onClose }: { category: Category | null; onClose: () => void }) {
  const save = useSaveCategory(category?.id ?? null);
  const teams = useTeams();
  const toast = useToast();
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { name: category?.name ?? '', description: category?.description ?? '', defaultTeamId: category?.defaultTeamId ?? '' },
  });

  async function onSubmit(values: z.infer<typeof schema>) {
    try {
      await save.mutateAsync({ name: values.name, description: values.description || null, defaultTeamId: values.defaultTeamId || null });
      toast.success(category ? 'Categoria atualizada.' : 'Categoria criada.');
      onClose();
    } catch {
      // toast global exibido
    }
  }

  return (
    <Modal open onOpenChange={(o) => !o && onClose()} title={category ? 'Editar categoria' : 'Nova categoria'}>
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
        <TextField label="Nome" required error={errors.name?.message} {...register('name')} />
        <TextAreaField label="Descrição" rows={3} error={errors.description?.message} {...register('description')} />
        <SelectField label="Equipe padrão" hint="Os chamados desta categoria são encaminhados a essa equipe." {...register('defaultTeamId')}>
          <option value="">Sem equipe padrão</option>
          {teams.data?.filter((t) => t.isActive).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
        </SelectField>
        <ModalFooter>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" loading={isSubmitting}>Salvar</Button>
        </ModalFooter>
      </form>
    </Modal>
  );
}

export default function CategoriesPage() {
  const toast = useToast();
  const categories = useCategories();
  const teams = useTeams();
  const toggle = useToggleCategory();
  const [form, setForm] = useState<Category | 'new' | null>(null);
  const [toggling, setToggling] = useState<Category | null>(null);
  const teamName = (id: string | null) => teams.data?.find((t) => t.id === id)?.name ?? '—';

  return (
    <>
      <PageHeader title="Categorias" description="Classificam os chamados e definem a equipe responsável." actions={<Button onClick={() => setForm('new')}><Plus className="size-4" aria-hidden /> Nova categoria</Button>} />
      <Card>
        {categories.isPending ? <SkeletonRows rows={6} /> : categories.isError ? <ErrorState error={categories.error} onRetry={() => void categories.refetch()} /> : categories.data.length === 0 ? (
          <EmptyState title="Nenhuma categoria" />
        ) : (
          <TableWrap>
            <Table>
              <caption className="sr-only">Categorias</caption>
              <thead><tr><Th>Categoria</Th><Th>Equipe padrão</Th><Th>Situação</Th><Th className="text-right">Ações</Th></tr></thead>
              <tbody>
                {categories.data.map((c) => (
                  <Tr key={c.id}>
                    <Td><p className="font-medium">{c.name}</p>{c.description && <p className="text-xs text-muted">{c.description}</p>}</Td>
                    <Td>{teamName(c.defaultTeamId)}</Td>
                    <Td><Badge tone={c.isActive ? 'success' : 'neutral'}>{c.isActive ? 'Ativa' : 'Desativada'}</Badge></Td>
                    <Td className="text-right whitespace-nowrap">
                      <Button variant="ghost" size="sm" onClick={() => setForm(c)} aria-label={`Editar ${c.name}`}>Editar</Button>
                      <Button variant="ghost" size="sm" onClick={() => setToggling(c)} aria-label={`${c.isActive ? 'Desativar' : 'Reativar'} ${c.name}`}>{c.isActive ? 'Desativar' : 'Reativar'}</Button>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          </TableWrap>
        )}
      </Card>

      {form && <CategoryModal category={form === 'new' ? null : form} onClose={() => setForm(null)} />}
      {toggling && (
        <ConfirmDialog
          open
          onOpenChange={(o) => !o && setToggling(null)}
          title={toggling.isActive ? `Desativar “${toggling.name}”?` : `Reativar “${toggling.name}”?`}
          description={toggling.isActive ? 'A categoria some da abertura de chamados. Chamados existentes não são afetados.' : 'A categoria volta a ficar disponível.'}
          confirmLabel={toggling.isActive ? 'Desativar' : 'Reativar'}
          tone={toggling.isActive ? 'danger' : 'primary'}
          onConfirm={async () => {
            await toggle.mutateAsync({ id: toggling.id, isActive: !toggling.isActive });
            toast.success(toggling.isActive ? 'Categoria desativada.' : 'Categoria reativada.');
          }}
        />
      )}
    </>
  );
}
