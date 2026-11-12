import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
import { z } from 'zod';
import { useCurrentUser } from '@/app/auth';
import { Avatar, PageHeader, RoleBadge } from '@/components/common';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { SelectField, TextField } from '@/components/ui/fields';
import { ConfirmDialog, Modal, ModalFooter } from '@/components/ui/modal';
import { EmptyState, ErrorState, SkeletonRows } from '@/components/ui/states';
import { Table, TableWrap, Td, Th, Tr } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { useCreateUser, useToggleUser, useUpdateUser, useUsers } from '@/features/insights/api';
import { ROLE_LABEL } from '@/lib/format';
import { passwordSchema } from '@/lib/validation';
import type { Role, User } from '@/types/api';

const ROLES = Object.keys(ROLE_LABEL) as Role[];

const createSchema = z.object({
  fullName: z.string().trim().min(2, 'Informe o nome.').max(150),
  email: z.string().min(1, 'Informe o e-mail.').pipe(z.email('E-mail inválido.')),
  role: z.enum(['ADMIN', 'AGENT', 'REQUESTER']),
  password: passwordSchema,
});
const editSchema = z.object({ fullName: z.string().trim().min(2, 'Informe o nome.').max(150), role: z.enum(['ADMIN', 'AGENT', 'REQUESTER']) });

function CreateUserModal({ onClose }: { onClose: () => void }) {
  const create = useCreateUser();
  const toast = useToast();
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof createSchema>>({
    resolver: zodResolver(createSchema),
    defaultValues: { fullName: '', email: '', role: 'REQUESTER', password: '' },
  });

  async function onSubmit(values: z.infer<typeof createSchema>) {
    try {
      await create.mutateAsync(values);
      toast.success('Usuário criado.');
      onClose();
    } catch {
      // toast global exibido
    }
  }

  return (
    <Modal open onOpenChange={(o) => !o && onClose()} title="Novo usuário" description="A senha inicial deve ser repassada com segurança; o usuário pode trocá-la no perfil.">
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
        <TextField label="Nome completo" required error={errors.fullName?.message} {...register('fullName')} />
        <TextField label="E-mail" type="email" required autoComplete="off" error={errors.email?.message} {...register('email')} />
        <SelectField label="Perfil de acesso" required {...register('role')}>
          {ROLES.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}
        </SelectField>
        <TextField label="Senha inicial" type="password" required autoComplete="new-password" hint="Mínimo de 10 caracteres, com maiúscula, minúscula, número e símbolo." error={errors.password?.message} {...register('password')} />
        <ModalFooter>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" loading={isSubmitting}>Criar usuário</Button>
        </ModalFooter>
      </form>
    </Modal>
  );
}

function EditUserModal({ user, isSelf, onClose }: { user: User; isSelf: boolean; onClose: () => void }) {
  const update = useUpdateUser(user.id);
  const toast = useToast();
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof editSchema>>({
    resolver: zodResolver(editSchema),
    defaultValues: { fullName: user.fullName, role: user.role },
  });

  async function onSubmit(values: z.infer<typeof editSchema>) {
    try {
      await update.mutateAsync(values);
      toast.success('Usuário atualizado.');
      onClose();
    } catch {
      // toast global exibido
    }
  }

  return (
    <Modal open onOpenChange={(o) => !o && onClose()} title="Editar usuário" description={user.email}>
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
        <TextField label="Nome completo" required error={errors.fullName?.message} {...register('fullName')} />
        <SelectField label="Perfil de acesso" disabled={isSelf} hint={isSelf ? 'Você não pode alterar o seu próprio perfil.' : 'Mudar o perfil encerra as sessões do usuário. Ao deixar de ser agente, ele sai das equipes.'} {...register('role')}>
          {ROLES.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}
        </SelectField>
        <ModalFooter>
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" loading={isSubmitting}>Salvar</Button>
        </ModalFooter>
      </form>
    </Modal>
  );
}

export default function UsersPage() {
  const me = useCurrentUser();
  const toast = useToast();
  const users = useUsers();
  const toggle = useToggleUser();
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<User | null>(null);
  const [toggling, setToggling] = useState<User | null>(null);

  return (
    <>
      <PageHeader
        title="Usuários"
        description="Gerencie contas, perfis de acesso e ativação."
        actions={<Button onClick={() => setCreating(true)}><Plus className="size-4" aria-hidden /> Novo usuário</Button>}
      />

      <Card>
        {users.isPending ? (
          <SkeletonRows rows={6} />
        ) : users.isError ? (
          <ErrorState error={users.error} onRetry={() => void users.refetch()} />
        ) : users.data.length === 0 ? (
          <EmptyState title="Nenhum usuário" />
        ) : (
          <TableWrap>
            <Table>
              <caption className="sr-only">Usuários cadastrados</caption>
              <thead>
                <tr><Th>Usuário</Th><Th>Perfil</Th><Th>Situação</Th><Th className="text-right">Ações</Th></tr>
              </thead>
              <tbody>
                {users.data.map((u) => (
                  <Tr key={u.id}>
                    <Td>
                      <div className="flex items-center gap-3">
                        <Avatar name={u.fullName} />
                        <div className="min-w-0">
                          <p className="truncate font-medium">{u.fullName}{u.id === me.id && <span className="ml-2 text-xs text-muted">(você)</span>}</p>
                          <p className="truncate text-xs text-muted">{u.email}</p>
                        </div>
                      </div>
                    </Td>
                    <Td><RoleBadge role={u.role} /></Td>
                    <Td><Badge tone={u.isActive ? 'success' : 'neutral'}>{u.isActive ? 'Ativo' : 'Desativado'}</Badge></Td>
                    <Td className="text-right whitespace-nowrap">
                      <Button variant="ghost" size="sm" onClick={() => setEditing(u)} aria-label={`Editar ${u.fullName}`}>Editar</Button>
                      <Button variant="ghost" size="sm" disabled={u.id === me.id && u.isActive} onClick={() => setToggling(u)} aria-label={`${u.isActive ? 'Desativar' : 'Reativar'} ${u.fullName}`}>
                        {u.isActive ? 'Desativar' : 'Reativar'}
                      </Button>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          </TableWrap>
        )}
      </Card>

      {creating && <CreateUserModal onClose={() => setCreating(false)} />}
      {editing && <EditUserModal user={editing} isSelf={editing.id === me.id} onClose={() => setEditing(null)} />}
      {toggling && (
        <ConfirmDialog
          open
          onOpenChange={(o) => !o && setToggling(null)}
          title={toggling.isActive ? `Desativar ${toggling.fullName}?` : `Reativar ${toggling.fullName}?`}
          description={toggling.isActive ? 'O usuário perde o acesso imediatamente e suas sessões são encerradas. O histórico é preservado.' : 'O usuário poderá entrar novamente com a senha atual.'}
          confirmLabel={toggling.isActive ? 'Desativar' : 'Reativar'}
          tone={toggling.isActive ? 'danger' : 'primary'}
          onConfirm={async () => {
            await toggle.mutateAsync({ id: toggling.id, isActive: !toggling.isActive });
            toast.success(toggling.isActive ? 'Usuário desativado.' : 'Usuário reativado.');
          }}
        />
      )}
    </>
  );
}
