import { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useAuth } from '@/app/auth';
import { Button } from '@/components/ui/button';
import { TextField } from '@/components/ui/fields';
import { ApiError } from '@/lib/api';
import { AuthLayout } from '@/layouts/AuthLayout';

const schema = z.object({
  email: z.string().min(1, 'Informe seu e-mail.').pipe(z.email('Informe um e-mail válido.')),
  password: z.string().min(1, 'Informe sua senha.'),
});

type FormValues = z.infer<typeof schema>;

function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return 'E-mail ou senha incorretos, ou a conta está bloqueada/desativada.';
    if (error.status === 429) return 'Muitas tentativas. Aguarde um minuto e tente novamente.';
  }
  return 'Não foi possível entrar agora. Verifique sua conexão e tente novamente.';
}

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '', password: '' } });

  const destination = (location.state as { from?: string } | null)?.from ?? '/';

  async function onSubmit(values: FormValues) {
    setFormError(null);
    try {
      await login(values.email.trim(), values.password);
      navigate(destination, { replace: true });
    } catch (error) {
      setFormError(errorMessage(error));
    }
  }

  return (
    <AuthLayout
      title="Entrar"
      description="Acesse com o e-mail e a senha da sua conta."
      footer={<Link to="/forgot-password" className="font-medium text-accent hover:underline">Esqueci minha senha</Link>}
    >
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
        {formError && (
          <div role="alert" className="rounded-lg border border-danger/40 bg-danger-soft px-3.5 py-2.5 text-[13px] text-danger">
            {formError}
          </div>
        )}
        <TextField label="E-mail" type="email" autoComplete="username" autoFocus error={errors.email?.message} {...register('email')} />
        <TextField label="Senha" type="password" autoComplete="current-password" error={errors.password?.message} {...register('password')} />
        <Button type="submit" loading={isSubmitting} className="mt-1 w-full">
          Entrar
        </Button>
      </form>
    </AuthLayout>
  );
}
