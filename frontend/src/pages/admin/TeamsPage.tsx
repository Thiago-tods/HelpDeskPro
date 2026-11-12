import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
import { z } from 'zod';
import { PageHeader } from '@/components/common';
import { Badge } from '@/components/ui/badge';
import { Button, IconButton } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { SelectField, TextAreaField, TextField } from '@/components/ui/fields';
import { ConfirmDialog, Modal, ModalFooter } from '@/components/ui/modal';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { Table, TableWrap, Td, Th, Tr } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { useAddMember, useAgents, useRemoveMember, useSaveTeam, useTeamMembers, useTeams, useToggleTeam } from '@/features/catalog/api';
import { formatDate } from '@/lib/format';
import type { Team } from '@/types/api';

const schema = z.object({ name: z.string().trim().min(2, 'Informe o nome.').max(100), description: z.string().trim().max(500) });

function TeamFormModal({ team, onClose }: { team: Team | null; onClose: () => void }) {
  const save = useSaveTeam(team?.id ?? null);
  const toast = useToast();
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { name: team?.name ?? '', description: team?.description ?? '' },
  });

  async function onSubmit(values: z.infer<typeof schema>) {
    try {
      await save.mutateAsync({ name: values.name, description: values.description || null });
      toast.success(team ? 'Equipe atualizada.' : 'Equipe criada.');
      onClose();
    } catch {
      // toast global exibido
    }
  }

  return (
    <Modal open onOpenChange={(o) => !o && onClose()} title={team ? 'Editar equipe' : 'Nova equipe'}>
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
        <TextField label="Nome" required error={errors.name?.message} {...register('name')} />
        <TextAreaField label="Descrição" rows={3} error={errors.description?.message} {...register('description')} />
        <ModalFooter>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" loading={isSubmitting}>Salvar</Button>
        </ModalFooter>
      </form>
    </Modal>
  );
}

function MembersModal({ team, onClose }: { team: Team; onClose: () => void }) {
  const members = useTeamMembers(team.id);
  const agents = useAgents();
  const add = useAddMember(team.id);
  const remove = useRemoveMember(team.id);
  const toast = useToast();
  const [selected, setSelected] = useState('');
  const [removing, setRemoving] = useState<{ userId: string; name: string } | null>(null);

  const available = agents.data?.filter((a) => !members.data?.some((m) => m.userId === a.id)) ?? [];

  return (
    <Modal open onOpenChange={(o) => !o && onClose()} title={`Membros — ${team.name}`} description="Somente agentes ativos podem integrar equipes." size="lg">
      <div className="flex flex-col gap-5">
        <div className="flex items-end gap-2">
          <SelectField label="Adicionar agente" wrapperClassName="flex-1" value={selected} onChange={(e) => setSelected(e.target.value)}>
            <option value="">Selecione…</option>
            {available.map((a) => <option key={a.id} value={a.id}>{a.fullName} ({a.email})</option>)}
          </SelectField>
          <Button
            disabled={!selected}
            loading={add.isPending}
            onClick={async () => {
              try {
                await add.mutateAsync(selected);
                toast.success('Membro adicionado.');
                setSelected('');
              } catch {
                // toast global exibido
              }
            }}
          >
            Adicionar
          </Button>
        </div>

        {members.isPending ? <SkeletonRows rows={3} /> : members.isError ? <ErrorState error={members.error} onRetry={() => void members.refetch()} /> : members.data.length === 0 ? (
          <EmptyState title="Equipe sem membros" description="Adicione agentes para que recebam os chamados da equipe." />
        ) : (
          <ul className="divide-y divide-border rounded-xl border border-border">
            {members.data.map((m) => (
              <li key={m.userId} className="flex items-center justify-between gap-3 px-4 py-3">
                <div className="min-w-0">
                  <p className="truncate text-[13px] font-medium">{m.fullName}</p>
                  <p className="truncate text-xs text-muted">{m.email} • desde {formatDate(m.joinedAt)}</p>
                </div>
                <IconButton label={`Remover ${m.fullName} da equipe`} onClick={() => setRemoving({ userId: m.userId, name: m.fullName })}>
                  <Trash2 className="size-4 text-danger" aria-hidden />
                </IconButton>
              </li>
            ))}
          </ul>
        )}
      </div>

      {removing && (
        <ConfirmDialog
          open
          onOpenChange={(o) => !o && setRemoving(null)}
          title={`Remover ${removing.name}?`}
          description="O agente deixa de receber os chamados desta equipe. Chamados já atribuídos a ele continuam com ele."
          confirmLabel="Remover"
          onConfirm={async () => {
            await remove.mutateAsync(removing.userId);
            toast.success('Membro removido.');
          }}
        />
      )}
    </Modal>
  );
}

export default function TeamsPage() {
  const toast = useToast();
  const teams = useTeams();
  const toggle = useToggleTeam();
  const [form, setForm] = useState<Team | 'new' | null>(null);
  const [membersOf, setMembersOf] = useState<Team | null>(null);
  const [toggling, setToggling] = useState<Team | null>(null);

  return (
    <>
      <PageHeader title="Equipes" description="Organize os agentes que atendem cada tipo de chamado." actions={<Button onClick={() => setForm('new')}><Plus className="size-4" aria-hidden /> Nova equipe</Button>} />
      <Card>
        {teams.isPending ? <SkeletonRows rows={4} /> : teams.isError ? <ErrorState error={teams.error} onRetry={() => void teams.refetch()} /> : teams.data.length === 0 ? (
          <EmptyState title="Nenhuma equipe" action={<Button onClick={() => setForm('new')}>Criar equipe</Button>} />
        ) : (
          <TableWrap>
            <Table>
              <caption className="sr-only">Equipes</caption>
              <thead><tr><Th>Equipe</Th><Th>Membros</Th><Th>Situação</Th><Th className="text-right">Ações</Th></tr></thead>
              <tbody>
                {teams.data.map((t) => (
                  <Tr key={t.id}>
                    <Td><p className="font-medium">{t.name}</p>{t.description && <p className="text-xs text-muted">{t.description}</p>}</Td>
                    <Td>{t.memberCount}</Td>
                    <Td><Badge tone={t.isActive ? 'success' : 'neutral'}>{t.isActive ? 'Ativa' : 'Desativada'}</Badge></Td>
                    <Td className="text-right whitespace-nowrap">
                      <Button variant="ghost" size="sm" onClick={() => setMembersOf(t)} aria-label={`Membros de ${t.name}`}>Membros</Button>
                      <Button variant="ghost" size="sm" onClick={() => setForm(t)} aria-label={`Editar ${t.name}`}>Editar</Button>
                      <Button variant="ghost" size="sm" onClick={() => setToggling(t)} aria-label={`${t.isActive ? 'Desativar' : 'Reativar'} ${t.name}`}>{t.isActive ? 'Desativar' : 'Reativar'}</Button>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          </TableWrap>
        )}
      </Card>

      {form && <TeamFormModal team={form === 'new' ? null : form} onClose={() => setForm(null)} />}
      {membersOf && <MembersModal team={membersOf} onClose={() => setMembersOf(null)} />}
      {toggling && (
        <ConfirmDialog
          open
          onOpenChange={(o) => !o && setToggling(null)}
          title={toggling.isActive ? `Desativar ${toggling.name}?` : `Reativar ${toggling.name}?`}
          description={toggling.isActive ? 'A equipe deixa de poder receber novos chamados. O histórico é preservado.' : 'A equipe volta a receber chamados.'}
          confirmLabel={toggling.isActive ? 'Desativar' : 'Reativar'}
          tone={toggling.isActive ? 'danger' : 'primary'}
          onConfirm={async () => {
            await toggle.mutateAsync({ id: toggling.id, isActive: !toggling.isActive });
            toast.success(toggling.isActive ? 'Equipe desativada.' : 'Equipe reativada.');
          }}
        />
      )}
    </>
  );
}
