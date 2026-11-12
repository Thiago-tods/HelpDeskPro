import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useToast } from '@/components/ui/toast';
import { Button } from '@/components/ui/button';
import { TextField } from '@/components/ui/fields';
import { ApiError, api, messageOf } from '@/lib/api';
import { AuthLayout } from '@/layouts/AuthLayout';
import { passwordSchema } from '@/lib/validation';

const schema = z
  .object({ newPassword: passwordSchema, confirm: z.string().min(1, 'Confirme a nova senha.') })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'As senhas não conferem.' });

type FormValues = z.infer<typeof schema>;

export default function ResetPasswordPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const toast = useToast();
  const [formError, setFormError] = useState<string | null>(null);

  const email = params.get('email') ?? '';
  const token = params.get('token') ?? '';

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { newPassword: '', confirm: '' } });

  async function onSubmit({ newPassword }: FormValues) {
    setFormError(null);
    try {
      await api('/auth/reset-password', { method: 'POST', body: { email, token, newPassword }, auth: false });
      toast.success('Senha redefinida. Entre com a nova senha.');
      navigate('/login', { replace: true });
    } catch (error) {
      setFormError(error instanceof ApiError && error.status === 400 ? messageOf(error) : 'Não foi possível redefinir a senha agora.');
    }
  }

  if (!email || !token) {
    return (
      <AuthLayout title="Link inválido" description="O link de redefinição está incompleto ou expirou." footer={<Link to="/forgot-password" className="font-medium text-accent hover:underline">Solicitar novo link</Link>}>
        <p className="text-[13px] text-muted">Gere uma nova redefinição para continuar.</p>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="Nova senha" description={`Defina a nova senha para ${email}.`} footer={<Link to="/login" className="font-medium text-accent hover:underline">Voltar ao login</Link>}>
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
        {formError && <div role="alert" className="rounded-lg border border-danger/40 bg-danger-soft px-3.5 py-2.5 text-[13px] text-danger">{formError}</div>}
        <TextField label="Nova senha" type="password" autoComplete="new-password" hint="Mínimo de 10 caracteres, com maiúscula, minúscula, número e símbolo." autoFocus error={errors.newPassword?.message} {...register('newPassword')} />
        <TextField label="Confirmar nova senha" type="password" autoComplete="new-password" error={errors.confirm?.message} {...register('confirm')} />
        <Button type="submit" loading={isSubmitting} className="w-full">Redefinir senha</Button>
      </form>
    </AuthLayout>
  );
}
