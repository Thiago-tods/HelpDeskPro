import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useAuth, useCurrentUser } from '@/app/auth';
import { PageHeader, RoleBadge } from '@/components/common';
import { Button } from '@/components/ui/button';
import { Card, CardBody, CardHeader } from '@/components/ui/card';
import { TextField } from '@/components/ui/fields';
import { ConfirmDialog } from '@/components/ui/modal';
import { useToast } from '@/components/ui/toast';
import { ApiError, api, messageOf } from '@/lib/api';
import { passwordSchema } from '@/lib/validation';
import type { AuthSession, User } from '@/types/api';

const nameSchema = z.object({ fullName: z.string().trim().min(2, 'Informe seu nome.').max(150, 'Use no máximo 150 caracteres.') });

const passwordFormSchema = z
  .object({ currentPassword: z.string().min(1, 'Informe a senha atual.'), newPassword: passwordSchema, confirm: z.string().min(1, 'Confirme a nova senha.') })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'As senhas não conferem.' })
  .refine((v) => v.newPassword !== v.currentPassword, { path: ['newPassword'], message: 'A nova senha deve ser diferente da atual.' });

function DataCard() {
  const user = useCurrentUser();
  const { setUser } = useAuth();
  const toast = useToast();
  const { register, handleSubmit, formState: { errors, isSubmitting, isDirty } } = useForm<z.infer<typeof nameSchema>>({
    resolver: zodResolver(nameSchema),
    defaultValues: { fullName: user.fullName },
  });

  async function onSubmit(values: z.infer<typeof nameSchema>) {
    try {
      const updated = await api<User>('/auth/me', { method: 'PATCH', body: values });
      setUser(updated);
      toast.success('Perfil atualizado.');
    } catch (error) {
      toast.error(messageOf(error));
    }
  }

  return (
    <Card>
      <CardHeader title="Dados pessoais" />
      <CardBody>
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          <TextField label="Nome completo" autoComplete="name" error={errors.fullName?.message} {...register('fullName')} />
          <TextField label="E-mail" value={user.email} readOnly disabled hint="O e-mail é gerenciado pelo administrador." />
          <div className="flex items-center gap-2 text-[13px]"><span className="text-muted">Perfil de acesso:</span> <RoleBadge role={user.role} /></div>
          <div><Button type="submit" loading={isSubmitting} disabled={!isDirty}>Salvar</Button></div>
        </form>
      </CardBody>
    </Card>
  );
}

function PasswordCard() {
  const { applySession } = useAuth();
  const toast = useToast();
  const { register, handleSubmit, reset, setError, formState: { errors, isSubmitting } } = useForm<z.infer<typeof passwordFormSchema>>({
    resolver: zodResolver(passwordFormSchema),
    defaultValues: { currentPassword: '', newPassword: '', confirm: '' },
  });

  async function onSubmit(values: z.infer<typeof passwordFormSchema>) {
    try {
      const session = await api<AuthSession>('/auth/change-password', {
        method: 'POST',
        body: { currentPassword: values.currentPassword, newPassword: values.newPassword },
      });
      applySession(session); // as demais sessões foram encerradas pelo servidor
      reset();
      toast.success('Senha alterada. Os outros dispositivos foram desconectados.');
    } catch (error) {
      const message = messageOf(error);
      if (error instanceof ApiError && error.status === 400) setError(message.includes('atual') ? 'currentPassword' : 'newPassword', { message });
      else toast.error(message);
    }
  }

  return (
    <Card>
      <CardHeader title="Alterar senha" description="Ao trocar a senha, as demais sessões são encerradas." />
      <CardBody>
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          <TextField label="Senha atual" type="password" autoComplete="current-password" error={errors.currentPassword?.message} {...register('currentPassword')} />
          <TextField label="Nova senha" type="password" autoComplete="new-password" hint="Mínimo de 10 caracteres, com maiúscula, minúscula, número e símbolo." error={errors.newPassword?.message} {...register('newPassword')} />
          <TextField label="Confirmar nova senha" type="password" autoComplete="new-password" error={errors.confirm?.message} {...register('confirm')} />
          <div><Button type="submit" loading={isSubmitting}>Alterar senha</Button></div>
        </form>
      </CardBody>
    </Card>
  );
}

function SessionsCard() {
  const { logout } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const [confirming, setConfirming] = useState(false);

  return (
    <Card>
      <CardHeader title="Sessões" description="Encerre o acesso em todos os dispositivos, inclusive neste." />
      <CardBody>
        <Button variant="danger" onClick={() => setConfirming(true)}>Encerrar todas as sessões</Button>
        <ConfirmDialog
          open={confirming}
          onOpenChange={setConfirming}
          title="Encerrar todas as sessões?"
          description="Você será desconectado de todos os dispositivos e precisará entrar novamente."
          confirmLabel="Encerrar sessões"
          onConfirm={async () => {
            try {
              await api('/auth/logout-all', { method: 'POST' });
            } catch (error) {
              toast.error(messageOf(error));
              throw error;
            }
            await logout();
            navigate('/login', { replace: true });
          }}
        />
      </CardBody>
    </Card>
  );
}

export default function ProfilePage() {
  return (
    <>
      <PageHeader title="Meu perfil" description="Seus dados, senha e sessões." />
      <div className="grid max-w-5xl gap-6 lg:grid-cols-2">
        <DataCard />
        <PasswordCard />
        <SessionsCard />
      </div>
    </>
  );
}
